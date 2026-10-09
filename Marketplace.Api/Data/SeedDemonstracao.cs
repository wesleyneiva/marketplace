using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;

namespace Marketplace.Api.Data;

// A empresa 1 (criada pela migration MultiEmpresa) é o "Marketplace" de demonstração: o simulador vende nela.
// Aqui criamos o VISITANTE da demonstração: perfil Gerente (vê quase todas as telas), SOMENTE LEITURA
// (não grava nada) e SEM senha (entra só pelo botão "Ver demonstração", quando Demonstracao:Ativa = true).
public static class SeedDemonstracao
{
    public const int EmpresaId = 1;
    public const string EmailPadrao = "visitante@marketplace.demo";

    public static async Task ExecutarAsync(IServiceProvider services)
    {
        using var scope = services.CriarEscopo(EmpresaId);
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var email = scope.ServiceProvider.GetRequiredService<IConfiguration>()["Demonstracao:Email"] ?? EmailPadrao;
        if (await usuarios.FindByEmailAsync(email) is not null)
            return;

        var visitante = new Usuario
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            NomeCompleto = "Visitante (demonstração)",
            SomenteLeitura = true,
        };
        await usuarios.CreateAsync(visitante); // sem senha
        await usuarios.AddToRoleAsync(visitante, Perfis.Gerente);
    }
}
