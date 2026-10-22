using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Comissão dos vendedores, por mês. Base = o que o cliente pagou por cada item (com a parte dele no desconto da venda);
// o % é o da CATEGORIA do produto, quando ela tem um próprio, senão o do vendedor. Troca de um item vendido por ele
// estorna a comissão daquele item (no mês da troca). Venda cancelada não entra. Fechar o mês lança uma conta a pagar
// por vendedor (categoria "Comissões de vendedores"), que vence no dia 5 do mês seguinte.
public class ComissaoService(AppDbContext db)
{
    public async Task<ComissaoMesResponse> CalcularAsync(DateOnly mes)
    {
        var inicio = new DateOnly(mes.Year, mes.Month, 1);
        var fim = inicio.AddMonths(1).AddDays(-1);
        var (ini, fimUtc) = (Relogio.InicioDoDiaUtc(inicio), Relogio.InicioDoDiaUtc(fim.AddDays(1)));
        var vendedores = await db.Vendedores.AsNoTracking().ToDictionaryAsync(v => v.Id);
        var categorias = await db.Categorias.AsNoTracking().Where(c => c.ComissaoPercentual != null)
            .ToDictionaryAsync(c => c.Id, c => c.ComissaoPercentual!.Value);

        var itens = await db.ItensVenda.AsNoTracking()
            .Where(i => i.Venda!.Status == StatusVenda.Concluida && i.Venda.VendedorId != null && i.Venda.DataHora >= ini && i.Venda.DataHora < fimUtc)
            .Select(i => new { i.VendaId, VendedorId = i.Venda!.VendedorId!.Value, i.Total, i.Venda.Subtotal, VendaTotal = i.Venda.Total, i.Produto!.CategoriaId })
            .ToListAsync();
        var trocas = await db.ItensTroca.AsNoTracking()
            .Where(i => i.Troca!.DataHora >= ini && i.Troca.DataHora < fimUtc && i.Troca.Venda!.VendedorId != null)
            .Select(i => new { VendedorId = i.Troca!.Venda!.VendedorId!.Value, i.Valor, i.Produto!.CategoriaId })
            .ToListAsync();
        var lancadas = await db.ComissoesMes.AsNoTracking().Where(c => c.Competencia == inicio).ToDictionaryAsync(c => c.VendedorId);

        decimal Pct(int vendedorId, int categoriaId) =>
            categorias.TryGetValue(categoriaId, out var p) ? p : vendedores.GetValueOrDefault(vendedorId)?.ComissaoPercentual ?? 0;

        var ids = itens.Select(i => i.VendedorId).Concat(trocas.Select(t => t.VendedorId)).Concat(lancadas.Keys)
            .Concat(vendedores.Values.Where(v => v.Ativo).Select(v => v.Id)).Distinct();
        var lista = ids.Select(id =>
        {
            var meus = itens.Where(i => i.VendedorId == id).ToList();
            var vendido = meus.GroupBy(i => i.VendaId).Sum(g => g.First().VendaTotal);
            var comissao = Dinheiro(meus.Sum(i => (i.Subtotal == 0 ? 0 : i.Total * i.VendaTotal / i.Subtotal) * Pct(id, i.CategoriaId) / 100m));
            var estorno = Dinheiro(trocas.Where(t => t.VendedorId == id).Sum(t => t.Valor * Pct(id, t.CategoriaId) / 100m));
            var v = vendedores.GetValueOrDefault(id);
            var lancada = lancadas.GetValueOrDefault(id);
            return new ComissaoVendedor(id, v?.Nome ?? "?", v?.ComissaoPercentual ?? 0, meus.Select(i => i.VendaId).Distinct().Count(),
                vendido, comissao, estorno, Math.Max(0, comissao - estorno), lancada is not null, lancada?.ContaPagarId, lancada?.Valor);
        }).OrderByDescending(c => c.Vendido).ThenBy(c => c.Nome).ToList();

        var semVendedor = db.Vendas.AsNoTracking()
            .Where(v => v.Status == StatusVenda.Concluida && v.VendedorId == null && v.Origem != OrigemVenda.Historico && v.DataHora >= ini && v.DataHora < fimUtc);
        return new ComissaoMesResponse($"{inicio:yyyy-MM}", inicio, fim, fim < Relogio.Hoje, lista, lista.Sum(c => c.Total),
            await semVendedor.CountAsync(), await semVendedor.SumAsync(v => (decimal?)v.Total) ?? 0);
    }

    // Fecha o mês: uma conta a pagar por vendedor com comissão (só os que ainda não foram lançados).
    public async Task<int> LancarAsync(DateOnly mes, string usuarioId)
    {
        var calculo = await CalcularAsync(mes);
        if (!calculo.MesFechado) throw new EstoqueException("O mês ainda não terminou: a comissão é lançada depois do último dia.");
        var categoria = await Financeiro.CategoriaAsync(db, ChavesFinanceiras.Comissoes);
        var vencimento = calculo.Inicio.AddMonths(1).AddDays(4); // dia 5 do mês seguinte
        if (vencimento < Relogio.Hoje) vencimento = Relogio.Hoje;
        var lancadas = 0;
        foreach (var c in calculo.Vendedores.Where(c => !c.Lancada && c.Total > 0))
        {
            var conta = new ContaPagar
            {
                Descricao = $"Comissão de {c.Nome} · {calculo.Inicio:MM/yyyy}", Documento = $"COM-{calculo.Inicio:yyyyMM}-{c.VendedorId}",
                Valor = c.Total, Vencimento = vencimento, CategoriaFinanceiraId = categoria, CriadaPorId = usuarioId,
                Observacao = $"{c.Vendas} venda(s), {c.Vendido:C} vendidos" + (c.EstornoTrocas > 0 ? $", {c.EstornoTrocas:C} estornados por trocas" : ""),
            };
            db.ComissoesMes.Add(new ComissaoMes { VendedorId = c.VendedorId, Competencia = calculo.Inicio, Valor = c.Total, ContaPagar = conta });
            lancadas++;
        }
        await db.SaveChangesAsync();
        return lancadas;
    }

    private static decimal Dinheiro(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
