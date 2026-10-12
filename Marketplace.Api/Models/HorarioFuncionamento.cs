namespace Marketplace.Api.Models;

// Horário de funcionamento da loja: cada dia da semana, o que fazer nos feriados e os feriados da cidade.
// Fica numa coluna só (jsonb) da Empresa: é sempre lido e gravado inteiro, nunca consultado por partes.
// Usado pelo simulador, pelo histórico, pelos relatórios ("média por dia aberto"), pelas compras (entrega em dia
// fechado vai para o próximo dia aberto) e pelos insights da IA.
public class HorarioFuncionamento
{
    // Sempre 7 itens, na ordem do DayOfWeek do .NET: 0 = domingo, 1 = segunda … 6 = sábado.
    public List<HorarioDia> Dias { get; set; } = [];

    // Nos feriados: "Fechado", "Domingo" (abre no horário de domingo) ou "Normal" (horário do dia da semana).
    public string Feriados { get; set; } = RegrasFeriado.Fechado;

    // Feriados da cidade/estado (os nacionais o sistema já conhece). Ex.: 20/09 Revolução Farroupilha (RS).
    public List<FeriadoLocal> FeriadosLocais { get; set; } = [];

    // O padrão de um mercadinho: segunda a sábado das 8h às 19h; domingo e feriado fechado.
    public static HorarioFuncionamento Padrao() => new()
    {
        Dias = Enumerable.Range(0, 7)
            .Select(d => new HorarioDia(d != 0, new TimeOnly(8, 0), new TimeOnly(19, 0)))
            .ToList(),
    };
}

// Fecha = hora em que a porta fecha (às 19:00 já está fechado). Fecha depois da meia-noite não é aceito.
public record HorarioDia(bool Aberto, TimeOnly Abre, TimeOnly Fecha);

public record FeriadoLocal(int Dia, int Mes, string Nome);

public static class RegrasFeriado
{
    public const string Fechado = "Fechado";
    public const string Domingo = "Domingo";
    public const string Normal = "Normal";
}
