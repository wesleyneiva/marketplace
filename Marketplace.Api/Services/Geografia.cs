using System.Text.Json;
using Marketplace.Api.Contracts;

namespace Marketplace.Api.Services;

// Cidades do Brasil pela busca da Open-Meteo (gratuita, a mesma do clima): nome, estado, coordenadas e FUSO.
public static class Geografia
{
    private static readonly Dictionary<string, string> Ufs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Acre"] = "AC", ["Alagoas"] = "AL", ["Amapá"] = "AP", ["Amazonas"] = "AM", ["Bahia"] = "BA", ["Ceará"] = "CE",
        ["Distrito Federal"] = "DF", ["Federal District"] = "DF", ["Espírito Santo"] = "ES", ["Goiás"] = "GO", ["Maranhão"] = "MA",
        ["Mato Grosso"] = "MT", ["Mato Grosso do Sul"] = "MS", ["Minas Gerais"] = "MG", ["Pará"] = "PA", ["Paraíba"] = "PB",
        ["Paraná"] = "PR", ["Pernambuco"] = "PE", ["Piauí"] = "PI", ["Rio de Janeiro"] = "RJ", ["Rio Grande do Norte"] = "RN",
        ["Rio Grande do Sul"] = "RS", ["Rondônia"] = "RO", ["Roraima"] = "RR", ["Santa Catarina"] = "SC", ["São Paulo"] = "SP",
        ["Sergipe"] = "SE", ["Tocantins"] = "TO",
    };

    public static async Task<List<CidadeResponse>> BuscarCidadesAsync(HttpClient http, string termo, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(termo)}&count=20&language=pt&format=json&countryCode=BR";
        using var resposta = await http.GetAsync(url, ct);
        resposta.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!json.RootElement.TryGetProperty("results", out var resultados)) return [];

        var lista = new List<CidadeResponse>();
        foreach (var r in resultados.EnumerateArray())
        {
            // Só lugares habitados (PPL…): fora "Base Aérea de Manaus", rios, morros…
            var tipo = r.TryGetProperty("feature_code", out var f) ? f.GetString() ?? "" : "";
            var estado = r.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "";
            var fuso = r.TryGetProperty("timezone", out var z) ? z.GetString() ?? "" : "";
            if (!tipo.StartsWith("PPL") || !Ufs.TryGetValue(estado, out var uf) || !FusoValido(fuso)) continue;
            lista.Add(new CidadeResponse(r.GetProperty("name").GetString()!, uf, estado,
                Math.Round(r.GetProperty("latitude").GetDecimal(), 5), Math.Round(r.GetProperty("longitude").GetDecimal(), 5), fuso,
                r.TryGetProperty("population", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null));
        }
        // As maiores primeiro (quem digita "São José" quase sempre quer uma cidade, não um povoado).
        return lista.OrderByDescending(c => c.Populacao ?? 0).DistinctBy(c => (c.Nome, c.Uf)).Take(10).ToList();
    }

    public static bool FusoValido(string fuso)
    {
        if (string.IsNullOrWhiteSpace(fuso) || !fuso.StartsWith("America/") && !fuso.StartsWith("Brazil/")) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(fuso); return true; }
        catch (TimeZoneNotFoundException) { return false; }
    }
}
