using Marketplace.Api.Controllers;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// Cria a ÁREA DO DONO da plataforma (uma "empresa" especial, que não é mercado) e o login do dono, pela linha de
// comando da VM — só quem tem acesso à VM consegue:
//
//   ./criar-dono.sh --email voce@exemplo.com --nome "Seu Nome"
//
// A senha provisória aparece só no terminal (troca obrigatória no primeiro acesso). Pode rodar de novo para
// acrescentar outra pessoa como dona (mesma empresa da plataforma).
public static class CriarDono
{
    public const string Comando = "criar-dono";

    public static async Task<int> ExecutarAsync(IServiceProvider services, IConfiguration args)
    {
        var email = args["email"]?.Trim();
        var nome = args["nome"]?.Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains('@') || string.IsNullOrEmpty(nome) || nome.Length < 2)
        {
            Console.Error.WriteLine("❌ Uso: criar-dono --email voce@exemplo.com --nome \"Seu Nome\"");
            return 1;
        }

        int empresaId;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == usuarios.NormalizeEmail(email)))
            {
                Console.Error.WriteLine($"❌ O e-mail {email} já é usuário do sistema.");
                return 1;
            }
            var plataforma = await db.Empresas.FirstOrDefaultAsync(e => e.Plataforma);
            if (plataforma is null)
            {
                plataforma = new Empresa { Nome = "WN Labs (plataforma)", Subdominio = "plataforma", LimiteCaixas = 1, Plataforma = true };
                db.Empresas.Add(plataforma);
                await db.SaveChangesAsync();
            }
            empresaId = plataforma.Id;
        }

        using (var scope = services.CriarEscopo(empresaId))
        {
            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var senha = UsuariosController.GerarSenhaProvisoria();
            var dono = new Usuario { UserName = email, Email = email, EmailConfirmed = true, NomeCompleto = nome, TrocarSenha = true };
            var criado = await usuarios.CreateAsync(dono, senha);
            if (!criado.Succeeded)
            {
                Console.Error.WriteLine("❌ " + string.Join(" ", criado.Errors.Select(AuthController.TraduzirErro)));
                return 1;
            }
            await usuarios.AddToRoleAsync(dono, Perfis.Administrador);
            Console.WriteLine($"""

                ✅ Área do dono pronta (empresa #{empresaId}).
                   Login: {email}
                   Senha provisória: {senha}   ← troque no primeiro acesso
                   Endereço: http://oracle-a1:5100  (só pela rede interna / Tailscale)

                """);
        }
        return 0;
    }
}
