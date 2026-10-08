using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// Dá um "começo de história" ao estoque do catálogo fictício. Só roda se não houver movimentações:
//  1. uma movimentação de Inventário para cada produto (o saldo atual passa a ter origem);
//  2. lotes de validade para os perecíveis — alguns vencendo/vencidos DE PROPÓSITO, para testar alertas.
public static class SeedEstoque
{
    // Produto → lotes (dias até vencer a partir de hoje, fração do estoque). Os não listados: 1 lote em 30 dias.
    private static readonly Dictionary<string, (int Dias, decimal Fracao)[]> Validades = new()
    {
        ["Iogurte natural 170g"] = [(-1, 0.15m), (12, 0.85m)],       // parte JÁ VENCIDA
        ["Presunto cozido fatiado"] = [(2, 1m)],                      // vence em 2 dias
        ["Alface crespa"] = [(1, 0.4m), (3, 0.6m)],
        ["Pão francês"] = [(0, 1m)],                                  // vence hoje
        ["Pão de forma 500g"] = [(4, 1m)],
        ["Bolo de laranja"] = [(3, 1m)],
        ["Banana prata"] = [(3, 0.5m), (6, 0.5m)],
        ["Tomate"] = [(4, 1m)],
        ["Maçã gala"] = [(9, 1m)],
        ["Laranja"] = [(10, 1m)],
        ["Batata"] = [(14, 1m)],
        ["Cebola"] = [(18, 1m)],
        ["Leite integral 1L"] = [(6, 0.25m), (20, 0.75m)],
        ["Queijo mussarela fatiado"] = [(5, 1m)],
        ["Requeijão cremoso 200g"] = [(25, 1m)],
        ["Manteiga com sal 200g"] = [(45, 1m)],
        ["Ovos brancos"] = [(16, 1m)],
        ["Carne moída de patinho"] = [(2, 1m)],
        ["Peito de frango"] = [(4, 1m)],
        ["Linguiça toscana"] = [(8, 1m)],
        ["Costela bovina"] = [(5, 1m)],
        ["Coxa e sobrecoxa de frango"] = [(3, 1m)],
        ["Água de coco 1L"] = [(6, 1m)],
        ["Suco de uva integral 1L"] = [(90, 1m)],
        ["Cerveja pilsen lata 350ml"] = [(120, 1m)],
        ["Cerveja puro malte long neck 355ml"] = [(150, 1m)],
        ["Biscoito cream cracker 350g"] = [(160, 1m)],
        ["Torrada integral 160g"] = [(60, 1m)],
    };

    public static async Task ExecutarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedEstoque");

        if (await db.Movimentacoes.AnyAsync())
            return;

        var hoje = Relogio.HojeBrasilia;
        var produtos = await db.Produtos.Where(p => p.EstoqueAtual > 0).ToListAsync();
        var totalLotes = 0;

        foreach (var produto in produtos)
        {
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id,
                Tipo = TipoMovimentacao.Inventario,
                Quantidade = produto.EstoqueAtual,
                EstoqueAnterior = 0,
                EstoquePosterior = produto.EstoqueAtual,
                CustoUnitario = produto.PrecoCusto,
                Observacao = "Estoque inicial (catálogo fictício)",
            });

            if (!produto.ControlaValidade)
                continue;

            var divisao = Validades.GetValueOrDefault(produto.Nome) ?? [(30, 1m)];
            var restante = produto.EstoqueAtual;
            for (var i = 0; i < divisao.Length; i++)
            {
                var (dias, fracao) = divisao[i];
                // O último lote fica com o que sobrou (evita sobra/falta por arredondamento).
                var quantidade = i == divisao.Length - 1
                    ? restante
                    : Arredondar(produto, produto.EstoqueAtual * fracao);
                restante -= quantidade;

                db.Lotes.Add(new LoteValidade
                {
                    ProdutoId = produto.Id,
                    DataValidade = hoje.AddDays(dias),
                    QuantidadeInicial = quantidade,
                    QuantidadeAtual = quantidade,
                });
                totalLotes++;
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Estoque inicial registrado: {Produtos} movimentações de inventário, {Lotes} lotes de validade.",
            produtos.Count, totalLotes);
    }

    private static decimal Arredondar(Produto produto, decimal quantidade) =>
        produto.Unidade is Unidades.Quilo or Unidades.Litro ? Math.Round(quantidade, 3) : Math.Round(quantidade);
}
