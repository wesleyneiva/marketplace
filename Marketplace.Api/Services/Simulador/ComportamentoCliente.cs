using Marketplace.Api.Contracts;
using Marketplace.Api.Models;

namespace Marketplace.Api.Services.Simulador;

// O "jeito de comprar" de um cliente, usado tanto pelo simulador AO VIVO quanto pelo gerador de HISTÓRICO.
// Regras num lugar só: se mudar aqui, muda nos dois.
public static class ComportamentoCliente
{
    // Clientes por hora num dia comum (pico no almoço e na saída do trabalho, antes de fechar às 19h).
    private static readonly Dictionary<int, double> ClientesPorHora = new()
    {
        [8] = 6, [9] = 6, [10] = 6, [11] = 9, [12] = 12, [13] = 8,
        [14] = 5, [15] = 5, [16] = 7, [17] = 12, [18] = 15,
    };

    // Feriados (mercado FECHADO): nacionais + os de Porto Alegre / RS.
    private static readonly HashSet<(int Dia, int Mes)> Feriados =
    [
        (1, 1), (2, 2) /* Navegantes, POA */, (21, 4), (1, 5), (7, 9), (20, 9) /* Farroupilha, RS */,
        (12, 10), (2, 11), (15, 11), (20, 11), (25, 12),
    ];

    public static bool Feriado(DateOnly dia) => Feriados.Contains((dia.Day, dia.Month));

    // Horário do mercado: segunda a sábado, das 8h às 19h. Domingo e feriado: fechado.
    public const int Abre = 8, Fecha = 19;

    public static bool AbertoNoDia(DateOnly dia) => dia.DayOfWeek != DayOfWeek.Sunday && !Feriado(dia);

    public static bool AbertoNaHora(DateOnly dia, int hora) => AbertoNoDia(dia) && hora >= Abre && hora < Fecha;

    // Média de clientes que chegam NESTA HORA (o simulador ao vivo divide por 60 para ter "por minuto").
    public static double ClientesNaHora(DateOnly dia, int hora, ClimaAgora clima, double intensidade)
    {
        var porHora = ClientesPorHora.GetValueOrDefault(hora, 3);
        porHora *= dia.DayOfWeek switch
        {
            DayOfWeek.Saturday => 1.35, // sem domingo, o sábado concentra as compras da semana
            DayOfWeek.Friday => 1.15,
            _ => 1.0,
        };
        porHora *= dia.Day <= 10 ? 1.15 : dia.Day >= 25 ? 0.9 : 1.0; // salário no começo do mês
        if (clima.Chovendo) porHora *= 0.75;                          // chuva: menos gente na rua
        return porHora * intensidade;
    }

    // Monta a cesta de um cliente. "disponivel" = quanto ele pode levar de cada produto
    // (ao vivo: o estoque dentro da validade; no histórico: sem limite).
    public static List<ItemVendaRequest> MontarCesta(
        IReadOnlyList<(Produto Produto, Habito Habito, decimal Disponivel)> opcoes, ClimaAgora clima, DateTimeOffset momento)
    {
        var itens = new List<ItemVendaRequest>();
        foreach (var (produto, habito, disponivel) in SortearProdutos(opcoes, clima, momento))
        {
            var quantidade = Math.Min(Quantidade(produto, habito), disponivel);
            if (produto.Unidade is not (Unidades.Quilo or Unidades.Litro)) quantidade = Math.Floor(quantidade);
            if (quantidade > 0) itens.Add(new ItemVendaRequest(produto.Id, quantidade));
        }
        return itens;
    }

    // Tamanho da cesta: muita gente leva 1 ou 2 coisas; alguns fazem "rancho".
    private static readonly int[] TamanhosDeCesta = [1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 5, 5, 6, 7, 8, 10];

    private static IEnumerable<(Produto, Habito, decimal)> SortearProdutos(
        IReadOnlyList<(Produto Produto, Habito Habito, decimal Disponivel)> opcoes, ClimaAgora clima, DateTimeOffset momento)
    {
        var tamanho = Math.Min(TamanhosDeCesta[Random.Shared.Next(TamanhosDeCesta.Length)], opcoes.Count);
        var pesos = opcoes.Select(o => o.Habito.Popularidade * PerfilConsumo.Multiplicador(o.Habito, clima, momento)).ToList();
        var restantes = Enumerable.Range(0, opcoes.Count).ToList();

        // Sorteio "com peso" e sem repetir: o pão francês sai muito mais que o shampoo.
        for (var n = 0; n < tamanho; n++)
        {
            var alvo = Random.Shared.NextDouble() * restantes.Sum(i => pesos[i]);
            foreach (var i in restantes)
            {
                alvo -= pesos[i];
                if (alvo > 0) continue;
                yield return opcoes[i];
                restantes.Remove(i);
                break;
            }
        }
    }

    private static decimal Quantidade(Produto produto, Habito habito)
    {
        if (produto.Unidade is Unidades.Quilo or Unidades.Litro)
        {
            var (min, max) = habito.PesoMax > 0 ? (habito.PesoMin, habito.PesoMax) : (0.3m, 1.5m);
            return Math.Round(min + (decimal)Random.Shared.NextDouble() * (max - min), 3); // peso de balança
        }
        if (habito.Pacotes is { Length: > 0 } pacotes)
            return pacotes[Random.Shared.Next(pacotes.Length)];
        return Random.Shared.NextDouble() < 0.85 ? 1 : 2;
    }

    // Pix 40% · Débito 30% · Crédito 15% · Dinheiro 15% (às vezes com nota "redonda" e troco).
    public static PagamentoRequest Pagamento(decimal total)
    {
        var sorteio = Random.Shared.NextDouble();
        if (sorteio < 0.40) return new("Pix", total);
        if (sorteio < 0.70) return new("Debito", total);
        if (sorteio < 0.85) return new("Credito", total);

        decimal[] notas = [2, 5, 10, 20, 50, 100, 200];
        var nota = notas.FirstOrDefault(n => n >= total);
        var paga = nota > 0 && Random.Shared.NextDouble() < 0.7 ? nota : total; // 30% pagam o valor exato
        return new("Dinheiro", paga);
    }

    // Quantos eventos acontecem, se a média é "lambda" (distribuição de Poisson).
    // Ex.: média 0,2 por minuto → na maioria dos minutos ninguém; às vezes 1; raramente 2.
    public static int Poisson(double lambda)
    {
        // Para médias grandes (histórico: clientes por HORA), aproxima pela normal — o método clássico
        // fica lento e impreciso quando e^-lambda é muito pequeno.
        if (lambda > 30)
            return Math.Max(0, (int)Math.Round(lambda + Math.Sqrt(lambda) * Normal()));

        var limite = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do
        {
            k++;
            p *= Random.Shared.NextDouble();
        } while (p > limite);
        return k - 1;
    }

    private static double Normal() =>
        Math.Sqrt(-2 * Math.Log(1 - Random.Shared.NextDouble())) * Math.Cos(2 * Math.PI * Random.Shared.NextDouble());
}
