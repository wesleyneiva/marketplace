namespace Marketplace.Api.Models;

// Clima de Porto Alegre, uma linha por hora (Open-Meteo). Guardado para cruzar com as vendas depois:
// "dias quentes vendem mais cerveja?" — a resposta vai estar nos dados.
public class ClimaRegistro
{
    public int Id { get; set; }
    public DateTimeOffset DataHora { get; set; }   // hora cheia (UTC)
    public decimal Temperatura { get; set; }       // °C
    public decimal Chuva { get; set; }             // mm na última hora
    public int CodigoTempo { get; set; }           // código WMO (0 = céu limpo, 61 = chuva...)
}
