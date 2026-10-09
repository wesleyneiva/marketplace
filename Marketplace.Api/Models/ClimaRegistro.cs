namespace Marketplace.Api.Models;

// Clima de uma cidade, uma linha por hora (Open-Meteo). Guardado para cruzar com as vendas depois:
// "dias quentes vendem mais cerveja?" — a resposta vai estar nos dados. O lugar é a latitude/longitude
// (arredondadas em 2 casas, ~1 km): duas lojas na mesma cidade usam as mesmas linhas.
public class ClimaRegistro
{
    public int Id { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTimeOffset DataHora { get; set; }   // hora cheia (UTC)
    public decimal Temperatura { get; set; }       // °C
    public decimal Chuva { get; set; }             // mm na última hora
    public int CodigoTempo { get; set; }           // código WMO (0 = céu limpo, 61 = chuva...)
}
