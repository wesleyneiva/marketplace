using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Trabalho em segundo plano: a cada 30 minutos registra o clima atual de CADA CIDADE que tem loja ativa
// (uma linha por hora, por lugar, na tabela Clima). Cidade nova (sem clima guardado): busca antes os últimos
// 60 dias, para o relatório "o clima influenciou?" já começar com dados. Lojas na mesma cidade dividem as linhas.
public class ColetorClima(IServiceScopeFactory escopos, ClimaService clima, ILogger<ColetorClima> log) : BackgroundService
{
    private readonly SemaphoreSlim _acordar = new(0, 1);

    // Uma loja mudou de cidade: coleta já (sem esperar a próxima rodada de 30 min).
    public void Acordar()
    {
        if (_acordar.CurrentCount == 0) _acordar.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct); // deixa a API terminar de subir
        while (!ct.IsCancellationRequested)
        {
            try { await ColetarAsync(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("Coletor de clima: {Erro}", e.Message); }
            await _acordar.WaitAsync(TimeSpan.FromMinutes(30), ct); // 30 min, ou antes se alguém chamar Acordar()
        }
    }

    private async Task ColetarAsync(CancellationToken ct)
    {
        List<LocalLoja> lugares;
        using (var scope = escopos.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            lugares = (await db.Empresas.AsNoTracking()
                    .Where(e => e.Ativa && e.Cidade != null && e.Latitude != null && e.Longitude != null)
                    .Select(e => new { e.Cidade, e.Uf, e.Latitude, e.Longitude, e.Fuso }).ToListAsync(ct))
                .Select(e => new LocalLoja(e.Cidade!, e.Uf, e.Latitude!.Value, e.Longitude!.Value, e.Fuso))
                .DistinctBy(l => (l.LatitudeClima, l.LongitudeClima)).ToList();
        }

        foreach (var lugar in lugares)
        {
            bool temHistorico;
            using (var scope = escopos.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var desde = DateTimeOffset.UtcNow.AddDays(-30);
                temHistorico = await db.Clima.AnyAsync(c => c.Latitude == lugar.LatitudeClima && c.Longitude == lugar.LongitudeClima && c.DataHora < desde, ct);
            }
            if (!temHistorico)
            {
                var horas = await clima.ObterHistoricoAsync(lugar, 60, ct);
                log.LogInformation("Clima de {Cidade}: {Horas} horas dos últimos 60 dias guardadas.", lugar.Nome, horas.Count);
            }
            await clima.ObterAsync(lugar, ct); // o de agora (registra a hora atual)
        }
    }
}
