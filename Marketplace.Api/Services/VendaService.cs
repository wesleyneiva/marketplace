using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services.Pix;
using Microsoft.EntityFrameworkCore;
using static Marketplace.Api.Models.ChavesFinanceiras;

namespace Marketplace.Api.Services;

// Finalizar e cancelar vendas. Uma venda grava, NUMA ÚNICA TRANSAÇÃO:
// o cupom (itens + pagamentos) e a baixa de estoque de cada produto (pelo EstoqueService).
public class VendaService(AppDbContext db, EstoqueService estoque, CaixaService caixa, FiadoService fiado,
    PixAutomaticoService pixAutomatico, AutorizacaoService autorizacoes)
{
    // Até quanto de desconto o perfil Caixa pode dar sozinho (acima disso, só Gerente/Administrador).
    public const decimal DescontoMaximoCaixa = 0.10m;

    public Task<Venda> FinalizarAsync(string usuarioId, bool podeDescontoLivre, NovaVendaRequest pedido,
        OrigemVenda origem = OrigemVenda.Caixa) =>
        ComRepeticaoAsync(() => TentarFinalizarAsync(usuarioId, podeDescontoLivre, pedido, origem));

    // Cancelar uma venda JÁ PAGA e estornar o dinheiro. usuarioId = quem está logado; autorizadoPorId = o gerente que liberou.
    public Task<Venda> CancelarAsync(int vendaId, string usuarioId, bool ehGerencia, string autorizadoPorId, string motivo, string? pix = null) =>
        ComRepeticaoAsync(() => TentarCancelarAsync(vendaId, usuarioId, ehGerencia, autorizadoPorId, motivo, pix));

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

    // ================================================================ cancelamento com estorno

    // Pix automático no estorno (ver CancelarVendaRequest).
    public const string PixDevolver = "Devolver", PixManter = "Manter", PixPorFora = "PorFora";

    // O plano do estorno: o que volta para o cliente, por onde, e se dá para fazer. A prévia (tela) e o cancelamento
    // usam a MESMA conta — o que a tela mostra é o que acontece.
    private sealed record LinhaEstorno(FormaPagamento Forma, decimal Valor, string Como, CobrancaPix? Cobranca = null);
    private sealed record PlanoEstorno(Venda Venda, string? Bloqueio, bool CaixaAberto, List<LinhaEstorno> Linhas,
        SessaoCaixa? GavetaDevolucao, bool LancarNoFinanceiro, string? Aviso);

    public async Task<EstornoPreviaResponse> PreviaEstornoAsync(int vendaId, string usuarioId, bool ehGerencia)
    {
        var p = await PlanejarEstornoAsync(vendaId, usuarioId, ehGerencia, null);
        return new EstornoPreviaResponse(p.Bloqueio is null, p.Bloqueio, p.CaixaAberto, !ehGerencia,
            p.Linhas.Select(l => new EstornoForma(l.Forma.ToString(), l.Valor, l.Como, l.Cobranca is not null)).ToList(), p.Aviso);
    }

    private async Task<PlanoEstorno> PlanejarEstornoAsync(int vendaId, string usuarioId, bool ehGerencia, string? pix)
    {
        var venda = await db.Vendas.Include(v => v.SessaoCaixa).Include(v => v.Pagamentos).Include(v => v.Cliente)
            .FirstOrDefaultAsync(v => v.Id == vendaId) ?? throw new EstoqueException("Venda não encontrada.");
        var sessao = venda.SessaoCaixa!;
        var aberto = sessao.Status == StatusSessao.Aberta;
        string? bloqueio = null;
        if (venda.Status == StatusVenda.Cancelada) bloqueio = "Esta venda já foi cancelada.";
        else if (venda.Origem == OrigemVenda.Historico) bloqueio = "Venda do histórico gerado: não pode ser cancelada (não movimentou estoque).";
        else if (!ehGerencia && (sessao.UsuarioId != usuarioId || !aberto))
            bloqueio = "No caixa só dá para cancelar as vendas do seu caixa aberto. As outras, o gerente cancela em Vendas.";

        pix ??= PixDevolver;
        if (pix is not (PixDevolver or PixManter or PixPorFora)) throw new EstoqueException("Opção do Pix inválida.");
        var cobrancas = await db.CobrancasPix.Where(c => c.VendaId == venda.Id).ToDictionaryAsync(c => c.Id);
        if (pix == PixManter && !aberto && cobrancas.Count > 0)
            bloqueio ??= "Com o caixa já fechado, o Pix não pode ficar para outra venda: devolva pelo Mercado Pago ou por fora.";

        // Dinheiro: o que o cliente deu menos o troco que já levou.
        var linhas = new List<LinhaEstorno>();
        SessaoCaixa? gavetaDevolucao = null;
        var dinheiro = venda.Pagamentos.Where(p => p.Forma == FormaPagamento.Dinheiro).Sum(p => p.Valor) - venda.Troco;
        if (dinheiro > 0)
        {
            string como;
            if (aberto)
            {
                // Sai da gaveta do caixa da venda: se não tem o suficiente (ex.: depois de uma sangria), não dá para devolver.
                var naGaveta = (await caixa.ResumirAsync(sessao.Id)).DinheiroEsperado;
                if (dinheiro > naGaveta)
                    bloqueio ??= $"A gaveta do Caixa {sessao.NumeroCaixa} tem {naGaveta:C}, mas o estorno devolve {dinheiro:C} em dinheiro. Faça um suprimento antes.";
                como = $"Sai da gaveta do Caixa {sessao.NumeroCaixa}";
            }
            else if ((gavetaDevolucao = await caixa.ObterSessaoAbertaAsync(usuarioId)) is not null)
            {
                var naGaveta = (await caixa.ResumirAsync(gavetaDevolucao.Id)).DinheiroEsperado;
                if (dinheiro > naGaveta)
                    bloqueio ??= $"A sua gaveta (Caixa {gavetaDevolucao.NumeroCaixa}) tem {naGaveta:C}, mas o estorno devolve {dinheiro:C} em dinheiro. Faça um suprimento antes.";
                como = $"Sai da sua gaveta (Caixa {gavetaDevolucao.NumeroCaixa})";
            }
            else como = "Sai do cofre da loja";
            linhas.Add(new LinhaEstorno(FormaPagamento.Dinheiro, dinheiro, como));
        }

        foreach (var p in venda.Pagamentos.Where(p => p.Forma != FormaPagamento.Dinheiro).OrderBy(p => p.Id))
        {
            var cobranca = p.CobrancaPixId is int id ? cobrancas.GetValueOrDefault(id) : null;
            var como = p.Forma switch
            {
                FormaPagamento.Pix when cobranca?.Status == StatusCobrancaPix.Devolvida => "Já devolvido pelo Mercado Pago",
                FormaPagamento.Pix when cobranca is not null => pix switch
                {
                    PixManter => "Fica como \"Pix recebido sem venda\" para usar numa nova compra",
                    PixPorFora => "A loja devolve ao cliente por conta própria",
                    _ => "Devolvido ao cliente pelo Mercado Pago, na hora",
                },
                FormaPagamento.Pix => "Devolva ao cliente pelo app do banco da loja",
                FormaPagamento.Fiado => $"Sai da caderneta{(venda.Cliente is null ? "" : " de " + venda.Cliente.Nome)}",
                _ => $"Estorne no {FinanceiroCaixa.NomeForma(p.Forma)} pela maquininha do cartão",
            };
            linhas.Add(new LinhaEstorno(p.Forma, p.Valor, como, cobranca));
        }

        // Caixa já fechado: as vendas dele já entraram no Financeiro → o estorno sai de lá hoje.
        // (Caixa fechado mas ainda não lançado — de antes da implantação, ou pendente — não lança: a venda cancelada não entra.)
        var lancar = false;
        string? aviso = null;
        if (!aberto)
        {
            var implantacao = await db.ContasFinanceiras.Where(c => c.Chave == ContaDinheiro).Select(c => (DateOnly?)c.SaldoInicialEm).FirstOrDefaultAsync();
            lancar = implantacao is not null
                     && (sessao.LancadaNoFinanceiroEm is not null || Relogio.DiaLocal(sessao.AbertaEm) < implantacao.Value);
            aviso = $"O Caixa {sessao.NumeroCaixa} desta venda já foi fechado"
                    + (lancar ? ": o estorno entra no Financeiro com a data de hoje." : ".");
        }
        return new PlanoEstorno(venda, bloqueio, aberto, linhas, gavetaDevolucao, lancar, aviso);
    }

    private async Task<Venda> TentarCancelarAsync(int vendaId, string usuarioId, bool ehGerencia, string autorizadoPorId, string motivo, string? pix)
    {
        var plano = await PlanejarEstornoAsync(vendaId, usuarioId, ehGerencia, pix);
        if (plano.Bloqueio is not null) throw new EstoqueException(plano.Bloqueio);
        var venda = plano.Venda;
        pix ??= PixDevolver;
        motivo = motivo.Trim();

        // Primeiro o que é de FORA (o Mercado Pago devolve o Pix): se falhar, nada foi mexido aqui.
        // (Se o resto falhar depois, a nova tentativa usa a mesma chave de idempotência: o Mercado Pago não devolve duas vezes.)
        foreach (var l in plano.Linhas.Where(l => l.Cobranca is not null))
        {
            if (pix == PixDevolver)
            {
                try { await pixAutomatico.DevolverAsync(l.Cobranca!); }
                catch (PixException e) { throw new EstoqueException(e.Message + " Tente de novo ou escolha \"a loja devolve por fora\"."); }
            }
            // Manter: o dinheiro continua na conta; o Pix volta a aparecer como "recebido sem venda" no caixa.
            else if (pix == PixManter) l.Cobranca!.VendaId = null;
        }

        venda.Status = StatusVenda.Cancelada;
        venda.CanceladaEm = DateTimeOffset.UtcNow;
        venda.CanceladaPorId = usuarioId;
        venda.MotivoCancelamento = motivo;

        await estoque.PrepararCancelamentoAsync(venda, usuarioId, motivo);
        await fiado.EstornarCompraAsync(venda, usuarioId, motivo);

        var descricao = $"Estorno da venda #{venda.Id} (Caixa {venda.SessaoCaixa!.NumeroCaixa}, {Relogio.DiaLocal(venda.DataHora):dd/MM})";
        var dinheiro = plano.Linhas.FirstOrDefault(l => l.Forma == FormaPagamento.Dinheiro)?.Valor ?? 0;
        if (dinheiro > 0 && plano.GavetaDevolucao is { } gaveta)
            db.MovimentosCaixa.Add(new MovimentoCaixa
            {
                SessaoCaixaId = gaveta.Id, Tipo = TipoMovimentoCaixa.Devolucao, Valor = dinheiro, UsuarioId = usuarioId,
                Motivo = $"Venda #{venda.Id} do Caixa {venda.SessaoCaixa.NumeroCaixa}",
            });
        if (plano.LancarNoFinanceiro)
            await LancarEstornoAsync(plano, descricao, usuarioId);

        autorizacoes.Registrar(AcaoAutorizada.CancelarVenda, usuarioId, autorizadoPorId, venda.SessaoCaixaId, motivo,
            $"Venda #{venda.Id} · {Itens(await db.ItensVenda.CountAsync(i => i.VendaId == venda.Id))} · "
            + string.Join(" + ", plano.Linhas.Select(l => FinanceiroCaixa.NomeForma(l.Forma))), venda.Total, venda);
        await db.SaveChangesAsync();
        return venda;
    }

    // Venda de caixa já lançado no Financeiro: o dinheiro que volta ao cliente sai da conta de cada forma, na categoria
    // "Vendas no PDV" (a receita da DRE vem das vendas concluídas: a cancelada já some de lá; isto acerta o saldo das contas).
    private async Task LancarEstornoAsync(PlanoEstorno plano, string descricao, string usuarioId)
    {
        var categoria = await Financeiro.CategoriaAsync(db, VendasPdv);
        var contas = await db.ConfiguracoesRecebimento.AsNoTracking().ToDictionaryAsync(c => c.Forma, c => c.ContaFinanceiraId);
        foreach (var l in plano.Linhas)
        {
            if (l.Forma == FormaPagamento.Fiado) continue;               // fiado não é dinheiro: sai da caderneta
            int conta;
            if (l.Forma == FormaPagamento.Dinheiro)
            {
                if (plano.GavetaDevolucao is not null) continue;        // saiu de uma gaveta aberta: lança no fechamento dela
                conta = await Financeiro.ContaAsync(db, ContaCofre);
            }
            else conta = contas.GetValueOrDefault(l.Forma) is var c and > 0 ? c : await Financeiro.ContaAsync(db, ContaBanco);
            db.MovimentosFinanceiros.Add(new MovimentoFinanceiro
            {
                Data = Relogio.Hoje, Tipo = TipoMovimentoFinanceiro.Saida, Valor = l.Valor, ContaFinanceiraId = conta,
                CategoriaFinanceiraId = categoria, Origem = OrigemMovimentoFinanceiro.Caixa, CriadoPorId = usuarioId,
                Descricao = $"{descricao} no {FinanceiroCaixa.NomeForma(l.Forma)}",
            });
        }
    }

    // ================================================================ cancelar no PDV (antes de pagar)

    // Tirar um item (ou parte dele) ou a compra inteira. Não mexe em estoque (nada foi vendido ainda): só REGISTRA,
    // com o valor calculado pelo preço do banco, quem pediu e quem autorizou.
    public async Task<decimal> RegistrarCancelamentoPdvAsync(string usuarioId, string autorizadoPorId, CancelamentoPdvRequest r)
    {
        var sessao = await caixa.ObterSessaoAbertaAsync(usuarioId) ?? throw new EstoqueException("Você não tem caixa aberto.");
        var acao = r.Tipo switch
        {
            "Item" => AcaoAutorizada.CancelarItem,
            "Compra" => AcaoAutorizada.CancelarCompra,
            _ => throw new EstoqueException("Tipo de cancelamento inválido (Item ou Compra)."),
        };
        if (acao == AcaoAutorizada.CancelarItem && r.Itens.Count != 1) throw new EstoqueException("Cancele um item por vez.");
        var ids = r.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        if (produtos.Count != ids.Count) throw new EstoqueException("Um dos produtos não existe.");

        var valor = r.Itens.Sum(i => Dinheiro(i.Quantidade * produtos[i.ProdutoId].PrecoVenda));
        string Linha(ItemVendaRequest i) => $"{i.Quantidade:0.###} {produtos[i.ProdutoId].Unidade} × {produtos[i.ProdutoId].Nome}";
        var descricao = acao == AcaoAutorizada.CancelarItem
            ? Linha(r.Itens[0])
            : $"Compra com {r.Itens.Count} {(r.Itens.Count == 1 ? "item" : "itens")}: " + string.Join("; ", r.Itens.Select(Linha));
        autorizacoes.Registrar(acao, usuarioId, autorizadoPorId, sessao.Id, r.Motivo, descricao, valor);
        await db.SaveChangesAsync();
        return valor;
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

    private static string Itens(int n) => n == 1 ? "1 item" : $"{n} itens";

    // Dinheiro sempre com 2 casas, arredondando 0,005 para cima (como a calculadora).
    private static decimal Dinheiro(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
