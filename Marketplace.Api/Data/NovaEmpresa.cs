using System.Text.RegularExpressions;
using Marketplace.Api.Controllers;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// Cadastro de um CLIENTE novo (uma empresa), pela linha de comando da VM — só quem tem acesso à VM consegue:
//
//   ./nova-empresa.sh --nome "Mercado do Zé" --subdominio mercadoze --caixas 2 \
//                     --admin-email ze@mercadoze.com.br --admin-nome "José da Silva"
//
// Cria a empresa, as categorias padrão (vazias) e o ADMINISTRADOR dela com senha provisória
// (mostrada uma vez; ele troca no primeiro acesso). Daí em diante, o próprio cliente cadastra o resto.
public static partial class NovaEmpresa
{
    public const string Comando = "nova-empresa";

    private static readonly string[] CategoriasPadrao =
        ["Mercearia", "Bebidas", "Hortifrúti", "Padaria", "Açougue", "Frios e Laticínios", "Limpeza", "Higiene e Beleza"];

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex SubdominioValido();

    public static async Task<int> ExecutarAsync(IServiceProvider services, IConfiguration args)
    {
        var nome = args["nome"]?.Trim();
        var subdominio = args["subdominio"]?.Trim().ToLowerInvariant();
        var caixas = args.GetValue("caixas", 2);
        var email = args["admin-email"]?.Trim();
        var nomeAdmin = args["admin-nome"]?.Trim();

        if (string.IsNullOrEmpty(nome) || string.IsNullOrEmpty(subdominio) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(nomeAdmin))
            return Erro("Uso: nova-empresa --nome \"Mercado do Zé\" --subdominio mercadoze --caixas 2 --admin-email ze@exemplo.com --admin-nome \"José\"");
        if (!SubdominioValido().IsMatch(subdominio))
            return Erro("Subdomínio: só letras minúsculas, números e hífen (ex.: mercadoze).");
        if (caixas is < 1 or > 999)
            return Erro("Caixas: de 1 a 999.");

        int empresaId;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Empresas.AnyAsync(e => e.Subdominio == subdominio))
                return Erro($"Já existe uma empresa com o subdomínio \"{subdominio}\".");
            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == usuarios.NormalizeEmail(email)))
                return Erro($"O e-mail {email} já é usuário do sistema.");

            var empresa = new Empresa { Nome = nome, Subdominio = subdominio, LimiteCaixas = caixas };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            empresaId = empresa.Id;
        }

        // Daqui em diante, tudo DENTRO da empresa nova (o carimbo automático põe o EmpresaId certo).
        using (var scope = services.CriarEscopo(empresaId))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Categorias.AddRange(CategoriasPadrao.Select(c => new Categoria { Nome = c }));
            await db.SaveChangesAsync();

            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var senha = UsuariosController.GerarSenhaProvisoria();
            var admin = new Usuario
            {
                UserName = email, Email = email, EmailConfirmed = true, NomeCompleto = nomeAdmin, TrocarSenha = true,
            };
            var criado = await usuarios.CreateAsync(admin, senha);
            if (!criado.Succeeded)
                return Erro("Empresa criada, mas o administrador não: " + string.Join(" ", criado.Errors.Select(AuthController.TraduzirErro)));
            await usuarios.AddToRoleAsync(admin, Perfis.Administrador);

            Console.WriteLine($"""

                ✅ Empresa criada: #{empresaId} {nome}
                   Subdomínio (futuro): {subdominio}.wnlabs.com.br
                   Caixas: {caixas}
                   Categorias padrão: {CategoriasPadrao.Length}

                   Administrador: {nomeAdmin} <{email}>
                   Senha provisória: {senha}   ← anote e passe ao cliente (troca no 1º acesso)

                """);
        }
        return 0;
    }

    private static int Erro(string mensagem)
    {
        Console.Error.WriteLine("❌ " + mensagem);
        return 1;
    }
}
