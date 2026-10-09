using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;

namespace Marketplace.Api.Data;

// Roda toda vez que a API liga: garante que os perfis existem e cria o administrador inicial
// (só se ainda não existir). A senha do admin vem da configuração (user-secrets), nunca do código.
public static class SeedInicial
{
    public static async Task ExecutarAsync(IServiceProvider services)
    {
        using var scope = services.CriarEscopo(SeedDemonstracao.EmpresaId);
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedInicial");

        foreach (var perfil in Perfis.Todos)
        {
            if (!await roleManager.RoleExistsAsync(perfil))
            {
                await roleManager.CreateAsync(new IdentityRole(perfil));
                logger.LogInformation("Perfil criado: {Perfil}", perfil);
            }
        }

        var email = config["AdminInicial:Email"];
        var senha = config["AdminInicial:Senha"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
        {
            logger.LogWarning("AdminInicial:Email/Senha não configurados: administrador inicial não foi criado.");
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var admin = new Usuario
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            NomeCompleto = config["AdminInicial:Nome"] ?? "Administrador"
        };

        var resultado = await userManager.CreateAsync(admin, senha);
        if (!resultado.Succeeded)
        {
            logger.LogError("Não foi possível criar o administrador: {Erros}",
                string.Join("; ", resultado.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, Perfis.Administrador);
        logger.LogInformation("Administrador inicial criado: {Email}", email);
    }
}
