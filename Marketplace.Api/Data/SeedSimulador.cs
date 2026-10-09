using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services.Simulador;
using Microsoft.AspNetCore.Identity;

namespace Marketplace.Api.Data;

// Cria o "operador" do simulador: perfil Caixa, SEM senha e bloqueado para login.
// Ele existe só para as vendas simuladas terem um responsável ("Caixa Automático").
public static class SeedSimulador
{
    public static async Task ExecutarAsync(IServiceProvider services)
    {
        using var scope = services.CriarEscopo(SeedDemonstracao.EmpresaId);
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        if (await usuarios.FindByEmailAsync(SimuladorEstado.Email) is not null)
            return;

        var robo = new Usuario
        {
            UserName = SimuladorEstado.Email,
            Email = SimuladorEstado.Email,
            EmailConfirmed = true,
            NomeCompleto = "Caixa Automático (simulador)",
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.MaxValue, // bloqueado para sempre: ninguém entra com ele
        };
        await usuarios.CreateAsync(robo); // sem senha
        await usuarios.AddToRoleAsync(robo, Perfis.Caixa);
    }
}
