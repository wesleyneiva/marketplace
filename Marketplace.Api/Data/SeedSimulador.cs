using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services.Simulador;
using Microsoft.AspNetCore.Identity;

namespace Marketplace.Api.Data;

// Cria o "operador" do simulador: perfil Caixa, SEM senha e bloqueado para login.
// Ele existe só para as vendas simuladas terem um responsável ("Caixa Automático").
public static class SeedSimulador
{
    public static async Task ExecutarAsync(IServiceProvider services) =>
        await GarantirRoboAsync(services.GetRequiredService<IServiceScopeFactory>(), SeedDemonstracao.EmpresaId);

    // Cria (se ainda não existir) o robô de caixa da empresa e devolve o Id dele.
    public static async Task<string> GarantirRoboAsync(IServiceScopeFactory escopos, int empresaId)
    {
        using var scope = escopos.CriarEscopo(empresaId);
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var email = SimuladorEstado.EmailDoRobo(empresaId);
        if (await usuarios.FindByEmailAsync(email) is { } existente)
            return existente.Id;

        var robo = new Usuario
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            NomeCompleto = "Caixa Automático (simulador)",
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.MaxValue, // bloqueado para sempre: ninguém entra com ele
        };
        await usuarios.CreateAsync(robo); // sem senha
        await usuarios.AddToRoleAsync(robo, Perfis.Caixa);
        return robo.Id;
    }
}
