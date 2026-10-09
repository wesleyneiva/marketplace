using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services.Simulador;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Compras: sugestão inteligente, pedido (rascunho → enviado → recebido) e a entrada no estoque.
// Erros de regra viram EstoqueException (o controller responde 400 com a mensagem).
public class ComprasService(AppDbContext db, EstoqueService estoque)
{
    public const int DiasDeCobertura = 7;   // compra o suficiente para o prazo de entrega + 7 dias de venda
    private const int DiasDeHistorico = 14; // consumo médio: últimos 14 dias

    // ---------------------------------------------------------------- sugestão

    // Para cada produto: quanto vende por dia, quantos dias o estoque ainda aguenta e o que já está a caminho.
    // Se o estoque (+ o que já foi pedido) não aguenta até o pedido chegar, sugere comprar.
    public async Task<List<SugestaoFornecedor>> SugestaoAsync(CancellationToken ct = default)
    {
        var hoje = Relogio.Hoje;
        var inicio = Relogio.InicioDoDiaUtc(hoje.AddDays(-DiasDeHistorico));
        var fim = Relogio.InicioDoDiaUtc(hoje);
        var diasAbertos = Enumerable.Range(1, DiasDeHistorico).Count(i => ComportamentoCliente.AbertoNoDia(hoje.AddDays(-i)));

        var vendido = await db.ItensVenda.AsNoTracking()
            .Where(i => i.Venda!.Status == StatusVenda.Concluida && i.Venda.DataHora >= inicio && i.Venda.DataHora < fim)
            .GroupBy(i => i.ProdutoId).Select(g => new { g.Key, Qtd = g.Sum(i => i.Quantidade) })
            .ToDictionaryAsync(x => x.Key, x => x.Qtd, ct);

        var aCaminho = await db.ItensPedidoCompra.AsNoTracking()
            .Where(i => i.PedidoCompra!.Status == StatusPedido.Rascunho || i.PedidoCompra.Status == StatusPedido.Enviado)
            .GroupBy(i => i.ProdutoId).Select(g => new { g.Key, Qtd = g.Sum(i => i.Quantidade) })
            .ToDictionaryAsync(x => x.Key, x => x.Qtd, ct);

        var produtos = await db.Produtos.AsNoTracking().Include(p => p.Fornecedor)
            .Where(p => p.Ativo && p.FornecedorId != null && p.Fornecedor!.Ativo)
            .ToListAsync(ct);

        var sugestoes = new List<(Fornecedor F, ItemSugestao Item)>();
        foreach (var p in produtos)
        {
            var consumo = diasAbertos == 0 ? 0 : vendido.GetValueOrDefault(p.Id) / diasAbertos;
            var pedido = aCaminho.GetValueOrDefault(p.Id);
            var prazo = p.Fornecedor!.PrazoEntregaDias;

            // Ponto de pedido: o que vai vender até o pedido chegar (+1 dia de folga) + o estoque mínimo.
            var pontoDePedido = consumo * (prazo + 1) + p.EstoqueMinimo;
            if (p.EstoqueAtual + pedido > pontoDePedido) continue;

            var necessario = consumo * (prazo + DiasDeCobertura) + p.EstoqueMinimo - p.EstoqueAtual - pedido;
            var sugerido = Arredondar(p, necessario);
            if (sugerido <= 0) continue;

            decimal? dias = consumo > 0 ? Math.Round(p.EstoqueAtual / consumo, 1) : null;
            var motivo = p.EstoqueAtual <= p.EstoqueMinimo
                ? "abaixo do mínimo"
                : dias is not null ? $"acaba em ~{dias:0.#} dia(s); entrega em {prazo}" : "perto do mínimo";

            sugestoes.Add((p.Fornecedor, new ItemSugestao(p.Id, p.Nome, p.Unidade, p.EstoqueAtual, p.EstoqueMinimo,
                Math.Round(consumo, 2), dias, pedido, sugerido, p.PrecoCusto, motivo)));
        }

        return sugestoes
            .GroupBy(s => s.F.Id)
            .Select(g => new SugestaoFornecedor(g.Key, g.First().F.Nome, g.First().F.PrazoEntregaDias,
                g.Sum(s => Math.Round(s.Item.Sugerido * s.Item.CustoUnitario, 2)),
                g.Select(s => s.Item).OrderBy(i => i.DiasDeEstoque ?? 0).ToList()))
            .OrderBy(s => s.Fornecedor)
            .ToList();
    }

    // ---------------------------------------------------------------- pedido

    public async Task<PedidoCompra> CriarAsync(NovoPedidoRequest r, string usuarioId, bool automatico = false)
    {
        var fornecedor = await db.Fornecedores.FirstOrDefaultAsync(f => f.Id == r.FornecedorId && f.Ativo)
            ?? throw new EstoqueException("Fornecedor não encontrado ou desativado.");

        var ids = r.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id) && p.Ativo).ToDictionaryAsync(p => p.Id);

        var pedido = new PedidoCompra
        {
            FornecedorId = fornecedor.Id, CriadoPorId = usuarioId, Automatico = automatico,
            Observacao = string.IsNullOrWhiteSpace(r.Observacao) ? null : r.Observacao.Trim(),
        };
        foreach (var item in r.Itens.GroupBy(i => i.ProdutoId))
        {
            if (!produtos.TryGetValue(item.Key, out var produto))
                throw new EstoqueException("Um dos produtos não existe ou está desativado.");
            var quantidade = item.Sum(i => i.Quantidade);
            if (produto.Unidade is not (Unidades.Quilo or Unidades.Litro) && quantidade % 1 != 0)
                throw new EstoqueException($"\"{produto.Nome}\" é comprado por {produto.Unidade}: use número inteiro.");
            pedido.Itens.Add(new ItemPedidoCompra
            {
                ProdutoId = produto.Id, Quantidade = quantidade,
                CustoUnitario = item.First().CustoUnitario is > 0 ? item.First().CustoUnitario!.Value : produto.PrecoCusto,
            });
        }

        db.PedidosCompra.Add(pedido);
        if (r.Enviar) MarcarEnviado(pedido, fornecedor);
        await db.SaveChangesAsync();
        return pedido;
    }

    public async Task EnviarAsync(int id)
    {
        var pedido = await CarregarAsync(id);
        if (pedido.Status != StatusPedido.Rascunho)
            throw new EstoqueException("Só um pedido em rascunho pode ser enviado.");
        MarcarEnviado(pedido, pedido.Fornecedor!);
        await db.SaveChangesAsync();
    }

    public async Task CancelarAsync(int id)
    {
        var pedido = await CarregarAsync(id);
        if (pedido.Status is StatusPedido.Recebido or StatusPedido.Cancelado)
            throw new EstoqueException($"Pedido já {pedido.Status.ToString().ToLower()}: não pode ser cancelado.");
        pedido.Status = StatusPedido.Cancelado;
        await db.SaveChangesAsync();
    }

    // Recebimento: confere item a item; o que chegou entra no estoque (com a validade, se perecível).
    // Tudo numa transação: ou o pedido inteiro entra, ou nada entra.
    public async Task ReceberAsync(int id, List<ItemRecebimento> conferidos, string usuarioId)
    {
        var pedido = await CarregarAsync(id);
        if (pedido.Status != StatusPedido.Enviado)
            throw new EstoqueException(pedido.Status == StatusPedido.Rascunho
                ? "Envie o pedido ao fornecedor antes de receber."
                : $"Pedido já {pedido.Status.ToString().ToLower()}.");

        await using var transacao = await db.Database.BeginTransactionAsync();
        foreach (var item in pedido.Itens)
        {
            var conferido = conferidos.FirstOrDefault(c => c.ItemId == item.Id)
                ?? throw new EstoqueException($"Falta conferir o item \"{item.Produto!.Nome}\".");
            item.QuantidadeRecebida = conferido.QuantidadeRecebida;
            item.Validade = conferido.Validade;
            if (conferido.CustoUnitario is > 0) item.CustoUnitario = conferido.CustoUnitario.Value;

            if (conferido.QuantidadeRecebida > 0)
                await estoque.RegistrarEntradaAsync(item.ProdutoId, conferido.QuantidadeRecebida, item.CustoUnitario,
                    conferido.Validade, $"Pedido de compra #{pedido.Id} — {pedido.Fornecedor!.Nome}", usuarioId);
        }

        pedido.Status = StatusPedido.Recebido;
        pedido.RecebidoEm = DateTimeOffset.UtcNow;
        pedido.RecebidoPorId = usuarioId;
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    // ---------------------------------------------------------------- apoio

    private static void MarcarEnviado(PedidoCompra pedido, Fornecedor fornecedor)
    {
        pedido.Status = StatusPedido.Enviado;
        pedido.EnviadoEm = DateTimeOffset.UtcNow;
        // Entrega: hoje + prazo; se cair em dia fechado (domingo/feriado), vai para o próximo dia aberto.
        var entrega = Relogio.Hoje.AddDays(fornecedor.PrazoEntregaDias);
        while (!ComportamentoCliente.AbertoNoDia(entrega)) entrega = entrega.AddDays(1);
        pedido.PrevisaoEntrega = entrega;
    }

    private async Task<PedidoCompra> CarregarAsync(int id) =>
        await db.PedidosCompra.Include(p => p.Fornecedor).Include(p => p.Itens).ThenInclude(i => i.Produto)
            .FirstOrDefaultAsync(p => p.Id == id)
        ?? throw new EstoqueException("Pedido não encontrado.");

    // UN/PCT/DZ: inteiro para cima. KG/L: de meio em meio (o fornecedor vende caixa de 0,5 kg em 0,5 kg).
    private static decimal Arredondar(Produto p, decimal quantidade) =>
        p.Unidade is Unidades.Quilo or Unidades.Litro ? Math.Ceiling(quantidade * 2) / 2 : Math.Ceiling(quantidade);
}
