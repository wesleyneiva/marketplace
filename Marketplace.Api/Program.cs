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
builder.Services.AddSingleton<Marketplace.Api.Services.LocaisDasLojas>();
builder.Services.AddScoped<ContextoEmpresa>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Marketplace")));

// Regras de estoque (usadas pelo EstoqueController e, no futuro, pelo PDV).
builder.Services.AddScoped<Marketplace.Api.Services.EstoqueService>();
builder.Services.AddScoped<Marketplace.Api.Services.CaixaService>();
builder.Services.AddScoped<Marketplace.Api.Services.VendaService>();
builder.Services.AddScoped<Marketplace.Api.Services.AutorizacaoService>(); // "chama o gerente": senha do gerente no caixa
builder.Services.AddScoped<Marketplace.Api.Services.ComprasService>();
builder.Services.AddScoped<Marketplace.Api.Services.Conciliacao.ConciliacaoService>();
builder.Services.AddScoped<Marketplace.Api.Services.Pix.MercadoPago>();
builder.Services.AddScoped<Marketplace.Api.Services.Pix.PixAutomaticoService>();
builder.Services.AddScoped<Marketplace.Api.Services.RelatoriosService>();
builder.Services.AddScoped<Marketplace.Api.Services.InsightsService>();
builder.Services.AddSingleton<Marketplace.Api.Services.InsightsTarefas>(); // insights da IA em segundo plano
builder.Services.AddScoped<Marketplace.Api.Services.CobrancasService>(); // mensalidades dos clientes (área do dono)
builder.Services.AddHostedService<Marketplace.Api.Services.CobrancasTrabalhador>();
builder.Services.AddHostedService<Marketplace.Api.Services.ComprasAutomaticas>(); // rascunhos de compra ao abrir a loja // gera mensalidades e bloqueia atraso, de hora em hora
builder.Services.AddScoped<Marketplace.Api.Services.ImportacaoProdutosService>();
builder.Services.AddScoped<Marketplace.Api.Services.NotaFiscal.EntradaNotaService>();

// Simulador de clientes (trabalhador em segundo plano) + clima real de Porto Alegre.
// Só simula se "Simulador:Ativo" = true (ligado no serviço de produção, desligado no desenvolvimento).
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Marketplace.Api.Services.ClimaService>();
builder.Services.AddSingleton<Marketplace.Api.Services.ColetorClima>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Marketplace.Api.Services.ColetorClima>()); // clima de cada cidade com loja
builder.Services.AddSingleton<Marketplace.Api.Services.LiquidadorRecebiveis>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Marketplace.Api.Services.LiquidadorRecebiveis>()); // caixas e maquininha → Financeiro, de hora em hora
builder.Services.AddSingleton<Marketplace.Api.Services.Simulador.SimuladorEstado>();
builder.Services.AddSingleton<Marketplace.Api.Services.Simulador.SimuladorClientes>();
builder.Services.AddScoped<Marketplace.Api.Services.Simulador.GeradorHistorico>();
builder.Services.AddScoped<Marketplace.Api.Services.Simulador.FinanceiroDemonstracao>();
builder.Services.AddScoped<Marketplace.Api.Services.Simulador.FiadoDemonstracao>();
builder.Services.AddScoped<Marketplace.Api.Services.FluxoCaixaService>();
builder.Services.AddScoped<Marketplace.Api.Services.DreService>();
builder.Services.AddScoped<Marketplace.Api.Services.FiadoService>();
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

// Qual é a empresa do dono da plataforma (área do dono). Ver Seguranca/DonoDaPlataforma.cs.
using (var scope = app.Services.CreateScope())
{
    Plataforma.Definir(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Empresas
        .Where(e => e.Plataforma).Select(e => (int?)e.Id).FirstOrDefaultAsync());
}

// Linha de comando: "criar-dono --email ... --nome ..." cria a área do dono da plataforma e o login dele. Ver Data/CriarDono.cs.
if (args.FirstOrDefault() == CriarDono.Comando)
{
    await SeedInicial.ExecutarAsync(app.Services);
    Environment.Exit(await CriarDono.ExecutarAsync(app.Services, app.Configuration));
}

// Linha de comando: "importar-produtos --empresa N --arquivo X [--gravar true]" — implantação de um cliente. Ver Data/ImportarProdutosCli.cs.
if (args.FirstOrDefault() == ImportarProdutosCli.Comando)
{
    Environment.Exit(await ImportarProdutosCli.ExecutarAsync(app.Services, app.Configuration));
}

// Linha de comando: "implantar-financeiro --arquivo X [--gravar true]" — saldos iniciais e contas fixas. Ver Data/ImplantarFinanceiroCli.cs.
if (args.FirstOrDefault() == ImplantarFinanceiroCli.Comando)
{
    Environment.Exit(await ImplantarFinanceiroCli.ExecutarAsync(app.Services, app.Configuration));
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
await SeedFinanceiro.ExecutarAsync(app.Services); // plano de contas e contas financeiras de cada empresa

// Linha de comando: "gerar-historico --dias 60 --substituir true" refaz o passado da DEMONSTRAÇÃO (vendas + financeiro)
// e sai. Ver gerar-historico.sh. Mesmo código do botão da API, sem precisar de login.
if (args.FirstOrDefault() == "gerar-historico")
{
    var empresaDemo = app.Configuration.GetValue("Simulador:EmpresaId", 1);
    using var escopo = app.Services.CriarEscopo(empresaDemo);
    var dbDemo = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
    Marketplace.Api.Services.Relogio.UsarFuso(await dbDemo.Empresas.Where(e => e.Id == empresaDemo).Select(e => e.Fuso).FirstAsync());
    var r = await escopo.ServiceProvider.GetRequiredService<Marketplace.Api.Services.Simulador.GeradorHistorico>()
        .GerarAsync(app.Configuration.GetValue("dias", 60), app.Configuration.GetValue("substituir", false), CancellationToken.None);
    Console.WriteLine($"✅ Histórico {r.De:dd/MM/yyyy} a {r.Ate:dd/MM/yyyy}: {r.Vendas} vendas, {r.Faturamento:C}. Financeiro: {r.Financeiro}.");
    Environment.Exit(0);
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
app.Use(AcessoPublico.Middleware);
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ----- Telas do Angular (já compiladas) servidas pela própria API, na pasta wwwroot -----
// Arquivos com "hash" no nome (main-ABC123.js) podem ficar 1 ano no cache (navegador e Cloudflare): o nome muda a
// cada publicação. O index.html nunca fica em cache; os outros (favicon.svg…) só 1 hora — se não, um logo novo
// demoraria até 1 ano para aparecer (aconteceu na troca para o Marcus).
var arquivosEstaticos = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var nome = ctx.File.Name;
        // sw.js e offline-arquivos.json (caixa offline) também nunca: é por eles que o navegador descobre a versão nova.
        ctx.Context.Response.Headers.CacheControl = nome is "index.html" or "sw.js" or "offline-arquivos.json" ? "no-cache"
            : System.Text.RegularExpressions.Regex.IsMatch(nome, @"^(main|chunk|styles|polyfills)-[A-Za-z0-9_-]{8}\.(js|css)$") ? "public, max-age=31536000, immutable"
            : "public, max-age=3600";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(arquivosEstaticos);

app.UseAuthentication();
app.Use(ContextoEmpresa.Middleware);      // de qual empresa é quem está logado
app.Use(RedeInterna.Middleware);          // rede interna: só o dono da plataforma
app.Use(MarcaSenhaProvisoria.Middleware); // senha provisória: só deixa trocar a senha
app.Use(SomenteLeitura.Middleware);       // visitante da demonstração: só olha
app.Use(ModulosLiberados.Middleware);     // módulos que o dono da plataforma não liberou para o cliente
app.UseAuthorization();

app.MapControllers();

// Endereço que não existe: em /api → 404 de verdade; fora de /api → index.html
// (quem cuida das rotas /dashboard, /pdv... é o Angular, no navegador).
app.MapFallback("/api/{**resto}", () => Results.NotFound());
app.MapFallbackToFile("index.html", arquivosEstaticos);

app.Run();
