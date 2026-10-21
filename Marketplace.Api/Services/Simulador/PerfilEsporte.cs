using Marketplace.Api.Models;

namespace Marketplace.Api.Services.Simulador;

// O "jeito de comprar" numa LOJA DE TÊNIS (ramo esporte), para o bot das lojas em teste. O mercado segue em
// PerfilConsumo/ComportamentoCliente; aqui só o que muda: bem menos clientes, cesta pequena e cara, bolas, overgrips e
// encordoamento girando muito, raquete e tênis de vez em quando, e os números do meio da grade saindo mais.
public static class PerfilEsporte
{
    // Clientes por hora num dia comum: movimento BAIXO/MODERADO (loja especializada ainda formando clientela, ~6–7
    // vendas num dia útil): pouco de manhã, mais no fim da tarde (depois do trabalho / antes da aula).
    private static readonly Dictionary<int, double> ClientesPorHora = new()
    {
        [8] = 0.2, [9] = 0.4, [10] = 0.6, [11] = 0.7, [12] = 0.8, [13] = 0.6, [14] = 0.6, [15] = 0.7,
        [16] = 0.9, [17] = 1.1, [18] = 1.0, [19] = 0.8, [20] = 0.5, [21] = 0.3,
    };

    public static double ClientesNaHora(DateOnly dia, int hora, ClimaAgora clima, double intensidade)
    {
        var porHora = ClientesPorHora.GetValueOrDefault(hora, 0.2);
        porHora *= dia.DayOfWeek switch
        {
            DayOfWeek.Saturday => 1.6, // sábado: dia de jogar e de comprar
            DayOfWeek.Sunday => 1.1,
            DayOfWeek.Friday => 1.15,
            _ => 1.0,
        };
        porHora *= dia.Day <= 10 ? 1.15 : 1.0;   // salário
        if (clima.Chovendo) porHora *= 0.8;      // chuva: menos gente na rua e nas quadras
        return porHora * intensidade;
    }

    // Quanto cada produto sai (pela palavra no nome; o primeiro que bater vale).
    // Peso por MODELO (na grade, repartido entre os tamanhos). Raquete e tênis são compras de vez em quando;
    // bola, overgrip, corda e encordoamento giram toda semana.
    private static readonly (string Palavra, Habito Habito)[] Regras =
    [
        ("encordoamento", new(2.0, Etiqueta.Nenhuma, Pacotes: [1])), // 1 raquete por ordem
        ("troca de grip", new(0.6, Etiqueta.Nenhuma, Pacotes: [1])),
        ("stencil", new(0.4, Etiqueta.Nenhuma, Pacotes: [1])),
        ("overgrip", new(2.4, Etiqueta.Nenhuma, Pacotes: [1, 1, 1, 2, 3])),
        ("bola", new(2.2, Etiqueta.Nenhuma, Pacotes: [1, 1, 1, 2, 2, 3])),
        ("corda", new(1.1, Etiqueta.Nenhuma)),
        ("munhequeira", new(0.5, Etiqueta.Nenhuma, Pacotes: [1, 1, 2])),
        ("antivibrador", new(0.7, Etiqueta.Nenhuma, Pacotes: [1, 1, 2])),
        ("fita", new(0.4, Etiqueta.Nenhuma)),
        ("meia", new(0.6, Etiqueta.Nenhuma, Pacotes: [1, 1, 2])),
        ("isotonico", new(1.3, Etiqueta.Gelado, Pacotes: [1, 1, 2])),
        ("agua", new(1.0, Etiqueta.Gelado, Pacotes: [1, 1, 2])),
        ("squeeze", new(0.25, Etiqueta.Nenhuma)),
        ("toalha", new(0.15, Etiqueta.Nenhuma)),
        ("bone", new(0.25, Etiqueta.Nenhuma)),
        ("viseira", new(0.2, Etiqueta.Nenhuma)),
        ("testeira", new(0.2, Etiqueta.Nenhuma)),
        ("grip", new(0.3, Etiqueta.Nenhuma)),
        ("camiseta", new(0.3, Etiqueta.Nenhuma)),
        ("regata", new(0.2, Etiqueta.Nenhuma)),
        ("short", new(0.2, Etiqueta.Nenhuma)),
        ("saia", new(0.15, Etiqueta.Nenhuma)),
        ("vestido", new(0.1, Etiqueta.Nenhuma)),
        ("raqueteira", new(0.06, Etiqueta.Nenhuma)),
        ("mochila", new(0.07, Etiqueta.Nenhuma)),
        ("bolsa", new(0.05, Etiqueta.Nenhuma)),
        ("raquete infantil", new(0.12, Etiqueta.Nenhuma)),
        ("raquete", new(0.08, Etiqueta.Nenhuma)),
        // Por último: "tênis" aparece no nome de muita coisa ("Bola de tênis", "Meia de tênis") que já casou acima.
        ("tenis", new(0.25, Etiqueta.Nenhuma)),       // calçado (por modelo; o peso é dividido entre os números)
    ];

    private static readonly Habito Padrao = new(0.3, Etiqueta.Nenhuma);

    public static Habito Para(Produto produto)
    {
        var nome = PerfilConsumo.SemAcento(produto.Modelo?.Nome ?? produto.Nome);
        foreach (var (palavra, habito) in Regras)
            if (nome.Contains(palavra))
                return habito;
        return Padrao;
    }

    // As opções do cliente: na grade, o peso do MODELO é repartido entre as variações (senão um tênis com 20 números
    // venderia 20 vezes mais), e os tamanhos do meio saem mais (curva de tamanhos).
    public static List<(Produto Produto, Habito Habito, decimal Disponivel)> Opcoes(IEnumerable<(Produto Produto, decimal Disponivel)> produtos)
    {
        var lista = produtos.ToList();
        var pesoDoTamanho = lista.Where(o => o.Produto.ModeloId is not null)
            .ToDictionary(o => o.Produto.Id, o => PesoDoTamanho(o.Produto.Tamanho));
        var somaPorModelo = lista.Where(o => o.Produto.ModeloId is not null)
            .GroupBy(o => o.Produto.ModeloId!.Value).ToDictionary(g => g.Key, g => g.Sum(o => pesoDoTamanho[o.Produto.Id]));
        return lista.Select(o =>
        {
            var h = Para(o.Produto);
            if (o.Produto.ModeloId is int m && somaPorModelo[m] > 0)
                h = h with { Popularidade = h.Popularidade * pesoDoTamanho[o.Produto.Id] / somaPorModelo[m] };
            return (o.Produto, h, o.Disponivel);
        }).ToList();
    }

    // Calçado: o 41/42 é o que mais sai (adulto); roupa: M e G; empunhadura: L2 e L3.
    private static double PesoDoTamanho(string? tamanho)
    {
        if (string.IsNullOrWhiteSpace(tamanho)) return 1;
        if (double.TryParse(tamanho.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= 30 and <= 50)
            return Math.Exp(-Math.Pow(n - 40.5, 2) / (2 * 2.2 * 2.2)) + 0.05;
        return PerfilConsumo.SemAcento(tamanho).ToUpperInvariant() switch
        {
            "PP" => 0.3, "P" => 0.8, "M" => 1.3, "G" => 1.1, "GG" => 0.5, "XG" or "XGG" => 0.25,
            "L1" => 0.5, "L2" => 1.2, "L3" => 1.2, "L4" => 0.6, "L0" => 0.2,
            _ => 1,
        };
    }

    // Quem pede encordoamento quase sempre leva a corda nova da loja (~70%); o resto traz a corda dele.
    public static void CompletarCesta(List<Contracts.ItemVendaRequest> itens,
        IReadOnlyList<(Produto Produto, Habito Habito, decimal Disponivel)> opcoes)
    {
        var porId = opcoes.ToDictionary(o => o.Produto.Id, o => o.Produto);
        var encordoa = itens.Any(i => porId.TryGetValue(i.ProdutoId, out var p) && p.Servico
            && PerfilConsumo.SemAcento(p.Nome).Contains("encordoamento"));
        var temCorda = itens.Any(i => porId.TryGetValue(i.ProdutoId, out var p) && p.Nome.StartsWith("Corda "));
        if (!encordoa || temCorda || Random.Shared.NextDouble() > 0.7) return;
        var cordas = opcoes.Where(o => o.Produto.Nome.StartsWith("Corda ") && o.Disponivel >= 1).ToList();
        if (cordas.Count > 0) itens.Add(new(cordas[Random.Shared.Next(cordas.Count)].Produto.Id, 1));
    }

    // Quase todo mundo leva 1 ou 2 coisas (bola + overgrip, raquete + encordoamento).
    public static readonly int[] TamanhosDeCesta = [1, 1, 1, 1, 1, 2, 2, 2, 3];

    // Ticket alto: muito crédito (parcelado na maquininha), Pix, débito e pouco dinheiro.
    public static Contracts.PagamentoRequest Pagamento(decimal total)
    {
        var sorteio = Random.Shared.NextDouble();
        if (total >= 300 && sorteio < 0.70) return new("Credito", total);
        if (sorteio < 0.40) return new("Pix", total);
        if (sorteio < 0.65) return new("Credito", total);
        if (sorteio < 0.90) return new("Debito", total);
        decimal[] notas = [10, 20, 50, 100, 200];
        var nota = notas.FirstOrDefault(n => n >= total);
        return new("Dinheiro", nota > 0 && Random.Shared.NextDouble() < 0.7 ? nota : total);
    }
}
