using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Finalizar e cancelar vendas. Uma venda grava, NUMA ÚNICA TRANSAÇÃO:
// o cupom (itens + pagamentos) e a baixa de estoque de cada produto (pelo EstoqueService).
public class VendaService(AppDbContext db, EstoqueService estoque, CaixaService caixa, FiadoService fiado)
{
    // Até quanto de desconto o perfil Caixa pode dar sozinho (acima disso, só Gerente/Administrador).
    public const decimal DescontoMaximoCaixa = 0.10m;

    public Task<Venda> FinalizarAsync(string usuarioId, bool podeDescontoLivre, NovaVendaRequest pedido,
        OrigemVenda origem = OrigemVenda.Caixa) =>
        ComRepeticaoAsync(() => TentarFinalizarAsync(usuarioId, podeDescontoLivre, pedido, origem));

    public Task<Venda> CancelarAsync(int vendaId, string usuarioId, string motivo) =>
        ComRepeticaoAsync(() => TentarCancelarAsync(vendaId, usuarioId, motivo));

    private async Task<Venda> TentarFinalizarAsync(string usuarioId, bool podeDescontoLivre, NovaVendaRequest pedido, OrigemVenda origem)
    {
        var sessao = await caixa.ObterSessaoAbertaAsync(usuarioId)
            ?? throw new EstoqueException("Abra o caixa antes de vender.");

        // O mesmo produto bipado duas vezes vira um item só.
        var itensAgrupados = pedido.Itens
            .GroupBy(i => i.ProdutoId)
            .Select(g => new { ProdutoId = g.Key, Quantidade = g.Sum(i => i.Quantidade) })
            .ToList();

        // TRAVA PESSIMISTA: "SELECT ... FOR UPDATE" segura as linhas destes produtos até o fim da
        // transação. Outro caixa vendendo o mesmo produto ESPERA alguns milissegundos (em vez de falhar).
        // Ordenar por Id evita "deadlock" (dois caixas travando os mesmos produtos em ordens diferentes).
        // (O "*" não traz a coluna de sistema xmin, que o EF usa como versão: por isso ela vai explícita.)
        await using var transacao = await db.Database.BeginTransactionAsync();
        var ids = itensAgrupados.Select(i => i.ProdutoId).Distinct().ToArray();
        var produtos = await db.Produtos
            .FromSql($"SELECT *, xmin FROM \"Produtos\" WHERE \"Id\" = ANY({ids}) AND \"EmpresaId\" = {db.EmpresaAtual} ORDER BY \"Id\" FOR UPDATE")
            .ToDictionaryAsync(p => p.Id);

        var venda = new Venda { SessaoCaixaId = sessao.Id, UsuarioId = usuarioId, Origem = origem };

        foreach (var item in itensAgrupados)
        {
            if (!produtos.TryGetValue(item.ProdutoId, out var produto) || !produto.Ativo)
                throw new EstoqueException("Um dos produtos não existe ou está desativado.");

            // O PREÇO VEM DO BANCO, nunca da tela: ninguém consegue "mandar" um preço mais barato.
            venda.Itens.Add(new ItemVenda
            {
                ProdutoId = produto.Id,
                Descricao = produto.Nome,
                Unidade = produto.Unidade,
                Quantidade = item.Quantidade,
                PrecoUnitario = produto.PrecoVenda,
                CustoUnitario = produto.PrecoCusto,
                Total = Dinheiro(item.Quantidade * produto.PrecoVenda),
            });
        }

        venda.Subtotal = venda.Itens.Sum(i => i.Total);
        venda.Desconto = Dinheiro(pedido.Desconto);
        if (venda.Desconto >= venda.Subtotal)
            throw new EstoqueException("O desconto não pode ser maior ou igual ao valor da compra.");
        if (!podeDescontoLivre && venda.Desconto > Dinheiro(venda.Subtotal * DescontoMaximoCaixa))
            throw new EstoqueException($"Desconto acima de {DescontoMaximoCaixa:P0} precisa de um gerente.");
        venda.Total = venda.Subtotal - venda.Desconto;

        // Primeiro os itens (quantidade, estoque, validade); só depois o dinheiro.
        // Assim o operador vê o problema real ("só 34 dentro da validade") e não "falta pagar".
        db.Vendas.Add(venda);
        foreach (var item in venda.Itens)
            await estoque.PrepararVendaAsync(produtos[item.ProdutoId], item.Quantidade, venda, usuarioId);

        PrepararPagamentos(venda, pedido.Pagamentos);
        await VincularPixAutomaticoAsync(venda, pedido.Pagamentos, sessao.Id);

        // Fiado: vira dívida na caderneta do cliente (limite, bloqueio e atraso: acima disso só gerente libera).
        venda.ClienteId = pedido.ClienteId;
        var valorFiado = venda.Pagamentos.Where(p => p.Forma == FormaPagamento.Fiado).Sum(p => p.Valor);
        if (valorFiado > 0)
            await fiado.RegistrarCompraAsync(venda, pedido.ClienteId, valorFiado, podeDescontoLivre, usuarioId);

        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return venda;
    }

    private static void PrepararPagamentos(Venda venda, List<PagamentoRequest> pagamentos)
    {
        foreach (var p in pagamentos)
        {
            if (!Enum.TryParse<FormaPagamento>(p.Forma, ignoreCase: true, out var forma))
                throw new EstoqueException($"Forma de pagamento inválida: {p.Forma}. Use Dinheiro, Pix, Debito, Credito ou Fiado.");
            if (p.CobrancaPixId is not null && forma != FormaPagamento.Pix)
                throw new EstoqueException("Cobrança Pix só vale para pagamento em Pix.");
            venda.Pagamentos.Add(new PagamentoVenda { Forma = forma, Valor = Dinheiro(p.Valor), CobrancaPixId = p.CobrancaPixId });
        }

        venda.ValorPago = venda.Pagamentos.Sum(p => p.Valor);
        if (venda.ValorPago < venda.Total)
            throw new EstoqueException($"Falta pagar {venda.Total - venda.ValorPago:C}.");

        // Troco só sai da gaveta: cartão e Pix não podem passar do valor da compra.
        var naoDinheiro = venda.Pagamentos.Where(p => p.Forma != FormaPagamento.Dinheiro).Sum(p => p.Valor);
        if (naoDinheiro > venda.Total)
            throw new EstoqueException("Cartão, PIX e fiado não podem passar do total (troco só em dinheiro).");

        venda.Troco = venda.ValorPago - venda.Total;
    }

    // Pix automático: o pagamento só entra se a cobrança estiver PAGA no Mercado Pago (conferido pelo servidor),
    // com o mesmo valor e sem ter entrado em outra venda. FOR UPDATE: dois caixas não usam o mesmo Pix ao mesmo tempo.
    private async Task VincularPixAutomaticoAsync(Venda venda, List<PagamentoRequest> pagamentos, int sessaoId)
    {
        var ids = pagamentos.Where(p => p.CobrancaPixId is not null).Select(p => p.CobrancaPixId!.Value).ToArray();
        if (ids.Length == 0) return;
        if (ids.Distinct().Count() != ids.Length) throw new EstoqueException("O mesmo Pix aparece duas vezes nesta venda.");
        var cobrancas = await db.CobrancasPix
            .FromSql($"SELECT * FROM \"CobrancasPix\" WHERE \"Id\" = ANY({ids}) AND \"EmpresaId\" = {db.EmpresaAtual} FOR UPDATE")
            .ToDictionaryAsync(c => c.Id);
        foreach (var p in venda.Pagamentos.Where(p => p.CobrancaPixId is not null))
        {
            if (!cobrancas.TryGetValue(p.CobrancaPixId!.Value, out var c)) throw new EstoqueException("Cobrança Pix não encontrada.");
            if (c.Status != StatusCobrancaPix.Paga) throw new EstoqueException("Este Pix ainda não foi pago no Mercado Pago.");
            if (c.VendaId is not null) throw new EstoqueException("Este Pix já foi usado em outra venda.");
            if (Math.Abs(c.Valor - p.Valor) >= 0.005m) throw new EstoqueException($"O Pix recebido foi de {c.Valor:C}, não de {p.Valor:C}.");
            if (c.SessaoCaixaId is int s && s != sessaoId) throw new EstoqueException("Este Pix foi gerado em outro caixa.");
            c.Venda = venda;
        }
    }

    private async Task<Venda> TentarCancelarAsync(int vendaId, string usuarioId, string motivo)
    {
        var venda = await db.Vendas.Include(v => v.SessaoCaixa).FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new EstoqueException("Venda não encontrada.");
        if (venda.Status == StatusVenda.Cancelada)
            throw new EstoqueException("Esta venda já foi cancelada.");
        if (venda.Origem == OrigemVenda.Historico)
            throw new EstoqueException("Venda do histórico gerado: não pode ser cancelada (não movimentou estoque).");
        if (venda.SessaoCaixa!.Status != StatusSessao.Aberta)
            throw new EstoqueException("Só é possível cancelar vendas de um caixa ainda aberto.");

        // Cancelar = devolver o dinheiro ao cliente. A parte em dinheiro sai da gaveta:
        // se a gaveta não tem o suficiente (ex.: depois de uma sangria), não dá para devolver.
        var dinheiroDaVenda = await db.PagamentosVenda
            .Where(p => p.VendaId == vendaId && p.Forma == FormaPagamento.Dinheiro)
            .SumAsync(p => (decimal?)p.Valor) ?? 0;
        var devolverEmDinheiro = dinheiroDaVenda - venda.Troco;
        if (devolverEmDinheiro > 0)
        {
            var naGaveta = (await caixa.ResumirAsync(venda.SessaoCaixaId)).DinheiroEsperado;
            if (devolverEmDinheiro > naGaveta)
                throw new EstoqueException(
                    $"A gaveta do Caixa {venda.SessaoCaixa.NumeroCaixa} tem {naGaveta:C}, mas o cancelamento devolve {devolverEmDinheiro:C} em dinheiro. Faça um suprimento antes.");
        }

        venda.Status = StatusVenda.Cancelada;
        venda.CanceladaEm = DateTimeOffset.UtcNow;
        venda.CanceladaPorId = usuarioId;
        venda.MotivoCancelamento = motivo.Trim();

        await estoque.PrepararCancelamentoAsync(venda, usuarioId, venda.MotivoCancelamento);
        await fiado.EstornarCompraAsync(venda, usuarioId, venda.MotivoCancelamento);
        // Pix automático: o dinheiro continua na conta; o Pix volta a aparecer como "recebido sem venda" no caixa
        // (para usar em outra venda ou devolver ao cliente pelo app do Mercado Pago).
        foreach (var c in await db.CobrancasPix.Where(c => c.VendaId == venda.Id).ToListAsync()) c.VendaId = null;
        await db.SaveChangesAsync();
        return venda;
    }

    // Rede de segurança: se mesmo assim o banco acusar conflito (xmin) — ex.: uma ENTRADA de estoque
    // no mesmo instante —, tentamos de novo (até 3 vezes) com os dados atualizados.
    private async Task<Venda> ComRepeticaoAsync(Func<Task<Venda>> operacao)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                return await operacao();
            }
            catch (DbUpdateConcurrencyException) when (tentativa < 3)
            {
                db.ChangeTracker.Clear(); // esquece o que foi lido e lê tudo de novo
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflitoEstoqueException("O sistema está muito disputado agora. Tente finalizar de novo.");
            }
        }
    }

    // Dinheiro sempre com 2 casas, arredondando 0,005 para cima (como a calculadora).
    private static decimal Dinheiro(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
