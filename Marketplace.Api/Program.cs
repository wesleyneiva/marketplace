using System.Threading.RateLimiting;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

// Números e dinheiro nas mensagens no formato brasileiro (R$ 1.234,56).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("pt-BR");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// Banco de dados: PostgreSQL via Entity Framework Core.
// A string de conexão (com a senha) NÃO fica no código: vem do "user-secrets" em desenvolvimento
// e, no serviço (produção), da variável ConnectionStrings__Marketplace em /etc/marketplace/marketplace.env.
// Multi-tenant: "de qual empresa é este pedido?" (um por requisição; o AppDbContext usa para filtrar tudo).
builder.Services.AddScoped<ContextoEmpresa>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Marketplace")));

// Regras de estoque (usadas pelo EstoqueController e, no futuro, pelo PDV).
builder.Services.AddScoped<Marketplace.Api.Services.EstoqueService>();
builder.Services.AddScoped<Marketplace.Api.Services.CaixaService>();
builder.Services.AddScoped<Marketplace.Api.Services.VendaService>();
builder.Services.AddScoped<Marketplace.Api.Services.ComprasService>();
builder.Services.AddScoped<Marketplace.Api.Services.RelatoriosService>();
builder.Services.AddScoped<Marketplace.Api.Services.InsightsService>();
builder.Services.AddScoped<Marketplace.Api.Services.ImportacaoProdutosService>();

// Simulador de clientes (trabalhador em segundo plano) + clima real de Porto Alegre.
// Só simula se "Simulador:Ativo" = true (ligado no serviço de produção, desligado no desenvolvimento).
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Marketplace.Api.Services.ClimaService>();
builder.Services.AddSingleton<Marketplace.Api.Services.Simulador.SimuladorEstado>();
builder.Services.AddSingleton<Marketplace.Api.Services.Simulador.SimuladorClientes>();
builder.Services.AddScoped<Marketplace.Api.Services.Simulador.GeradorHistorico>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Marketplace.Api.Services.Simulador.SimuladorClientes>());

// Login e perfis: ASP.NET Core Identity, guardando usuários no nosso banco.
builder.Services.AddIdentity<Usuario, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<Marketplace.Api.Seguranca.MarcaSenhaProvisoria>();

// A cada 1 minuto, o cookie de quem está logado é conferido contra o banco: usuário desativado
// (ou que trocou a senha em outro lugar) cai em até 1 minuto. O padrão do Identity é 30 minutos.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "marketplace.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8); // um turno de trabalho
    options.SlidingExpiration = true;

    // Numa API, "não logado" deve ser 401 e "sem permissão" 403 (e não um redirecionamento para uma página de login).
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Chaves que "assinam" o cookie de login. Ficam numa pasta fixa: assim, reiniciar a API
// (ou a VM) NÃO desloga ninguém, e o modo desenvolvimento e o serviço usam as mesmas chaves.
var pastaChaves = builder.Configuration["DataProtection:Pasta"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/marketplace/chaves");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(pastaChaves))
    .SetApplicationName("Marketplace");

// Pela internet (Cloudflare Tunnel): o cloudflared avisa no cabeçalho X-Forwarded-Proto que o visitante
// usou HTTPS. Com isso o cookie de login sai marcado "Secure" (só trafega criptografado).
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedProto);

// Limite de pedidos por pessoa (IP real), contra robôs tentando senhas ou derrubando o site:
//  • login / demonstração: 10 por minuto;
//  • qualquer pedido vindo da internet: 300 por minuto (pelo Tailscale, sem limite).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, _) => new ValueTask(ctx.HttpContext.Response.WriteAsJsonAsync(
        new { mensagem = "Muitas tentativas seguidas. Espere um minuto e tente de novo." }));
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.IpReal(),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => ctx.VeioDaInternet()
        ? RateLimitPartition.GetFixedWindowLimiter(ctx.IpReal(),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) })
        : RateLimitPartition.GetNoLimiter("interno"));
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// No serviço, a API aplica sozinha as migrations pendentes ao ligar (o "dotnet ef database update").
if (app.Configuration.GetValue<bool>("Banco:MigrarAoIniciar"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Linha de comando: "nova-empresa ..." cadastra um cliente novo e sai (não liga o site). Ver Data/NovaEmpresa.cs.
if (args.FirstOrDefault() == NovaEmpresa.Comando)
{
    await SeedInicial.ExecutarAsync(app.Services); // garante os perfis (Administrador, Gerente, Caixa)
    Environment.Exit(await NovaEmpresa.ExecutarAsync(app.Services, app.Configuration));
}

await SeedInicial.ExecutarAsync(app.Services);
await SeedCatalogo.ExecutarAsync(app.Services);
await SeedEstoque.ExecutarAsync(app.Services);
await SeedSimulador.ExecutarAsync(app.Services);
await SeedFornecedores.ExecutarAsync(app.Services);
await SeedDemonstracao.ExecutarAsync(app.Services);

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
app.Use(AcessoPublico.Middleware);
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ----- Telas do Angular (já compiladas) servidas pela própria API, na pasta wwwroot -----
// Arquivos com "hash" no nome (main-ABC123.js) podem ficar 1 ano no cache do navegador;
// o index.html nunca fica em cache, para cada publicação nova aparecer na hora.
var arquivosEstaticos = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var nome = ctx.File.Name;
        ctx.Context.Response.Headers.CacheControl = nome == "index.html"
            ? "no-cache"
            : "public, max-age=31536000, immutable";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(arquivosEstaticos);

app.UseAuthentication();
app.Use(ContextoEmpresa.Middleware);      // de qual empresa é quem está logado
app.Use(MarcaSenhaProvisoria.Middleware); // senha provisória: só deixa trocar a senha
app.Use(SomenteLeitura.Middleware);       // visitante da demonstração: só olha
app.UseAuthorization();

app.MapControllers();

// Endereço que não existe: em /api → 404 de verdade; fora de /api → index.html
// (quem cuida das rotas /dashboard, /pdv... é o Angular, no navegador).
app.MapFallback("/api/{**resto}", () => Results.NotFound());
app.MapFallbackToFile("index.html", arquivosEstaticos);

app.Run();
