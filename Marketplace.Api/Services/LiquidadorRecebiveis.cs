using Marketplace.Api.Data;
using Marketplace.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Trabalho em segundo plano: de hora em hora (e logo depois de a API subir), deixa o Financeiro de CADA
// empresa em dia — lança caixas fechados pendentes e faz a "maquininha depositar" os recebíveis que venceram.
// É o que acontece na vida real: o dinheiro do débito simplesmente aparece no banco no dia seguinte.
public class LiquidadorRecebiveis(IServiceScopeFactory escopos, ILogger<LiquidadorRecebiveis> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct); // deixa a API terminar de subir
        while (!ct.IsCancellationRequested)
        {
            try { await RodadaAsync(ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Financeiro (rodada de hora em hora): {Erro}; tento na próxima hora.", e.Message);
            }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }

    public async Task RodadaAsync(CancellationToken ct)
    {
        List<(int Id, string Fuso)> empresas;
        using (var scope = escopos.CreateScope())
        {
            empresas = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Empresas.AsNoTracking()
                .Where(e => e.Ativa).Select(e => new { e.Id, e.Fuso }).ToListAsync(ct)).Select(e => (e.Id, e.Fuso)).ToList();
        }
        foreach (var (id, fuso) in empresas)
        {
            try
            {
                using var scope = escopos.CriarEscopo(id);
                Relogio.UsarFuso(fuso); // "hoje" no calendário da loja
                var (caixas, recebiveis) = await FinanceiroCaixa.PorEmDiaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct);
                if (caixas + recebiveis > 0)
                    log.LogInformation("Financeiro da empresa {Empresa}: {Caixas} caixa(s) lançado(s), {Recebiveis} recebível(is) recebido(s).", id, caixas, recebiveis);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Financeiro da empresa {Empresa}: {Erro}", id, e.Message); // uma empresa com problema não para as outras
            }
            finally
            {
                Relogio.UsarFuso(null);
            }
        }
    }
}
