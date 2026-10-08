using System.Text.Json;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

public record ClimaAgora(decimal Temperatura, decimal Chuva, int Codigo, string Descricao, bool Real)
{
    public bool Quente => Temperatura >= 28;
    public bool Frio => Temperatura <= 15;
    public bool Chovendo => Chuva > 0.2m || Codigo is >= 51 and <= 67 or >= 80 and <= 82 or >= 95;

    // Se a internet falhar, o simulador segue com um dia "neutro".
    public static readonly ClimaAgora Neutro = new(22, 0, 1, "sem dados (clima neutro)", false);
}

// Busca o clima atual de Porto Alegre na Open-Meteo (a mesma do Briefing do n8n), guarda 30 min
// em memória e registra uma linha por hora no banco.
public class ClimaService(IHttpClientFactory http, IServiceScopeFactory escopos, ILogger<ClimaService> log)
{
    private const string Url = "https://api.open-meteo.com/v1/forecast?latitude=-30.03&longitude=-51.23"
        + "&current=temperature_2m,precipitation,weather_code&timezone=America%2FSao_Paulo";

    private ClimaAgora? _ultimo;
    private DateTimeOffset _buscadoEm;

    public ClimaAgora? Ultimo => _ultimo;

    public async Task<ClimaAgora> ObterAsync(CancellationToken ct = default)
    {
        if (_ultimo is not null && DateTimeOffset.UtcNow - _buscadoEm < TimeSpan.FromMinutes(30))
            return _ultimo;

        try
        {
            using var resposta = await http.CreateClient().GetAsync(Url, ct);
            resposta.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var atual = json.RootElement.GetProperty("current");
            var codigo = atual.GetProperty("weather_code").GetInt32();
            _ultimo = new ClimaAgora(
                Math.Round(atual.GetProperty("temperature_2m").GetDecimal(), 1),
                Math.Round(atual.GetProperty("precipitation").GetDecimal(), 1),
                codigo, Descrever(codigo), Real: true);
            _buscadoEm = DateTimeOffset.UtcNow;
            await RegistrarHoraAsync(_ultimo, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Não foi possível obter o clima: {Erro}", e.Message);
            _ultimo ??= ClimaAgora.Neutro;
            _buscadoEm = DateTimeOffset.UtcNow.AddMinutes(-25); // tenta de novo em ~5 min
        }
        return _ultimo;
    }

    private async Task RegistrarHoraAsync(ClimaAgora clima, CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        var hora = new DateTimeOffset(agora.Year, agora.Month, agora.Day, agora.Hour, 0, 0, TimeSpan.Zero);

        using var scope = escopos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Clima.AnyAsync(c => c.DataHora == hora, ct))
            return;
        db.Clima.Add(new ClimaRegistro { DataHora = hora, Temperatura = clima.Temperatura, Chuva = clima.Chuva, CodigoTempo = clima.Codigo });
        await db.SaveChangesAsync(ct);
    }

    // Códigos WMO (os mesmos explicados na ferramenta de clima do seu /pergunta).
    private static string Descrever(int codigo) => codigo switch
    {
        0 => "céu limpo",
        1 or 2 => "poucas nuvens",
        3 => "nublado",
        45 or 48 => "neblina",
        >= 51 and <= 57 => "garoa",
        >= 61 and <= 67 => "chuva",
        >= 71 and <= 77 => "neve",
        >= 80 and <= 82 => "pancadas de chuva",
        >= 95 => "temporal",
        _ => "tempo instável",
    };
}
