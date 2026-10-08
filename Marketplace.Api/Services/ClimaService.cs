using System.Text.Json;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

public record PrevisaoDia(DateOnly Data, decimal Minima, decimal Maxima, decimal Chuva, int ChanceChuva, string Tempo);

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

    // Clima HORA A HORA dos últimos N dias. Junta duas fontes da Open-Meteo:
    //  • archive-api: o histórico "oficial" (completo, mas com alguns dias de atraso);
    //  • forecast com past_days: cobre os últimos dias que o arquivo ainda não tem.
    // Grava na tabela Clima as horas que faltam e devolve tudo por hora (UTC).
    public async Task<Dictionary<DateTimeOffset, ClimaAgora>> ObterHistoricoAsync(int dias, CancellationToken ct = default)
    {
        const string local = "latitude=-30.03&longitude=-51.23&hourly=temperature_2m,precipitation,weather_code&timezone=America%2FSao_Paulo";
        var hoje = Relogio.HojeBrasilia;
        var inicio = hoje.AddDays(-Math.Clamp(dias, 1, 90));

        var porHora = await LerHorasAsync(
            $"https://archive-api.open-meteo.com/v1/archive?{local}&start_date={inicio:yyyy-MM-dd}&end_date={hoje.AddDays(-1):yyyy-MM-dd}", ct);
        foreach (var (hora, c) in await LerHorasAsync($"https://api.open-meteo.com/v1/forecast?{local}&past_days=14&forecast_days=1", ct))
            porHora.TryAdd(hora, c); // só preenche as horas que o arquivo ainda não tem

        using var scope = escopos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var primeira = porHora.Keys.Min();
        var existentes = (await db.Clima.Where(c => c.DataHora >= primeira).Select(c => c.DataHora).ToListAsync(ct)).ToHashSet();
        foreach (var (hora, c) in porHora.Where(x => x.Key <= DateTimeOffset.UtcNow && !existentes.Contains(x.Key)))
            db.Clima.Add(new ClimaRegistro { DataHora = hora, Temperatura = c.Temperatura, Chuva = c.Chuva, CodigoTempo = c.Codigo });
        await db.SaveChangesAsync(ct);
        return porHora;
    }

    private async Task<Dictionary<DateTimeOffset, ClimaAgora>> LerHorasAsync(string url, CancellationToken ct)
    {
        using var resposta = await http.CreateClient().GetAsync(url, ct);
        resposta.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var h = json.RootElement.GetProperty("hourly");
        var horas = h.GetProperty("time").EnumerateArray().ToList();
        var temps = h.GetProperty("temperature_2m").EnumerateArray().ToList();
        var chuvas = h.GetProperty("precipitation").EnumerateArray().ToList();
        var codigos = h.GetProperty("weather_code").EnumerateArray().ToList();
        var fuso = TimeSpan.FromSeconds(json.RootElement.GetProperty("utc_offset_seconds").GetInt32());

        var porHora = new Dictionary<DateTimeOffset, ClimaAgora>();
        for (var i = 0; i < horas.Count; i++)
        {
            if (temps[i].ValueKind == JsonValueKind.Null) continue; // hora sem dado (futuro ou ainda não arquivada)
            var horaLocal = DateTime.Parse(horas[i].GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var codigo = codigos[i].ValueKind == JsonValueKind.Null ? 1 : codigos[i].GetInt32();
            porHora[new DateTimeOffset(horaLocal, fuso).ToUniversalTime()] = new ClimaAgora(
                Math.Round(temps[i].GetDecimal(), 1),
                chuvas[i].ValueKind == JsonValueKind.Null ? 0 : Math.Round(chuvas[i].GetDecimal(), 1),
                codigo, Descrever(codigo), Real: true);
        }
        return porHora;
    }

    // Previsão dos próximos dias (máx/mín, chuva) — usada pelos insights da IA ("sábado quente: reforce bebidas").
    public async Task<List<PrevisaoDia>> PrevisaoAsync(int dias, CancellationToken ct = default)
    {
        var url = "https://api.open-meteo.com/v1/forecast?latitude=-30.03&longitude=-51.23&timezone=America%2FSao_Paulo"
            + "&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,weather_code"
            + $"&forecast_days={Math.Clamp(dias, 1, 14)}";
        using var resposta = await http.CreateClient().GetAsync(url, ct);
        resposta.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var d = json.RootElement.GetProperty("daily");
        decimal Num(string campo, int i) => d.GetProperty(campo)[i].ValueKind == JsonValueKind.Null ? 0 : d.GetProperty(campo)[i].GetDecimal();

        var lista = new List<PrevisaoDia>();
        var datas = d.GetProperty("time").EnumerateArray().ToList();
        for (var i = 0; i < datas.Count; i++)
        {
            var codigo = (int)Num("weather_code", i);
            lista.Add(new PrevisaoDia(DateOnly.Parse(datas[i].GetString()!), Math.Round(Num("temperature_2m_min", i), 0),
                Math.Round(Num("temperature_2m_max", i), 0), Math.Round(Num("precipitation_sum", i), 1),
                (int)Num("precipitation_probability_max", i), Descrever(codigo)));
        }
        return lista;
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
