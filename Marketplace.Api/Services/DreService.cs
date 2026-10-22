using System.Globalization;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// DRE gerencial em REGIME DE COMPETÊNCIA: cada coisa conta no mês a que pertence, não no mês em que o dinheiro se mexeu.
//  • Receita = vendas concluídas no mês (o crédito que cai daqui a 30 dias é receita HOJE).
//  • Devoluções = trocas do mês (o crédito do vale-troca sai da receita e o custo dos itens volta ao CMV); o saldo de
//    vale-troca que venceu sem uso volta como receita no mês em que venceu.
//  • Impostos = receita × alíquota efetiva do Simples (o DAS de setembro, pago em outubro, é imposto de setembro).
//  • CMV = custo de cada item VENDIDO (comprar mercadoria não é despesa: vira estoque).
//  • Perdas = vencidos, avarias e ajustes de inventário, pelo custo.
//  • Despesas = contas a pagar pelo VENCIMENTO (pagas ou não) + lançamentos avulsos pela data + juros/descontos dos
//    pagamentos + taxas da maquininha pela data da VENDA (recebíveis). Os depósitos da maquininha não entram (seriam em dobro).
public class DreService(AppDbContext db)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] NomesMeses = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    private static readonly GrupoFinanceiro[] DespesasOperacionais =
        [GrupoFinanceiro.Pessoal, GrupoFinanceiro.Ocupacao, GrupoFinanceiro.Administrativas, GrupoFinanceiro.DespesasVendas];

    public async Task<DreResponse> CalcularAsync(DateOnly ateMes, int meses)
    {
        meses = Math.Clamp(meses, 1, 12);
        var hoje = Relogio.Hoje;
        var lista = Enumerable.Range(0, meses).Select(i => new DateOnly(ateMes.Year, ateMes.Month, 1).AddMonths(i - meses + 1)).ToList();
        // Meses inteiros antes de a empresa ter qualquer dado (venda ou implantação do financeiro) não entram: seriam colunas de zero.
        var primeiraVenda = await db.Vendas.MinAsync(v => (DateTimeOffset?)v.DataHora);
        var implantacao = await db.ContasFinanceiras.MinAsync(c => (DateOnly?)c.SaldoInicialEm);
        var inicioDados = new[] { primeiraVenda is { } pv ? Relogio.DiaLocal(pv) : (DateOnly?)null, implantacao }.Where(d => d is not null).Min();
        if (inicioDados is DateOnly primeiroDia && lista.Any(m => m.AddMonths(1) > primeiroDia))
            lista = lista.Where(m => m.AddMonths(1) > primeiroDia).ToList();
        var aliquota = await db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.AliquotaSimples).FirstAsync();
        var categorias = await db.CategoriasFinanceiras.AsNoTracking().ToListAsync();
        var porGrupo = categorias.GroupBy(c => c.Grupo).ToDictionary(g => g.Key, g => g.OrderBy(c => Ordem(c.Codigo)).ToList());

        var mesesDre = new List<MesDre>();
        var col = new List<Dictionary<string, decimal>>(); // por mês: chave da linha → valor
        foreach (var inicio in lista)
        {
            var fim = inicio.AddMonths(1).AddDays(-1);
            var v = new Dictionary<string, decimal>();
            var (ini, fimUtc) = (Relogio.InicioDoDiaUtc(inicio), Relogio.InicioDoDiaUtc(fim.AddDays(1)));

            // Vendas
            var vendas = db.Vendas.AsNoTracking().Where(x => x.Status == StatusVenda.Concluida && x.DataHora >= ini && x.DataHora < fimUtc);
            var subtotal = await vendas.SumAsync(x => (decimal?)x.Subtotal) ?? 0;
            var desconto = await vendas.SumAsync(x => (decimal?)x.Desconto) ?? 0;
            var devolvido = await db.Trocas.AsNoTracking().Where(t => t.DataHora >= ini && t.DataHora < fimUtc).SumAsync(t => (decimal?)t.Valor) ?? 0;
            var valesVencidos = await ValesVencidosAsync(inicio, fim, hoje);
            v["receita-bruta"] = subtotal;
            v["descontos"] = -desconto;
            v["devolucoes"] = -devolvido + valesVencidos;
            var receita = subtotal - desconto + v["devolucoes"];

            // CMV (sem o custo do que voltou nas trocas) e perdas
            v["cmv"] = -(await db.ItensVenda.AsNoTracking()
                .Where(i => i.Venda!.Status == StatusVenda.Concluida && i.Venda.DataHora >= ini && i.Venda.DataHora < fimUtc)
                .SumAsync(i => (decimal?)(i.CustoUnitario * i.Quantidade)) ?? 0)
                + (await db.ItensTroca.AsNoTracking().Where(i => i.Troca!.DataHora >= ini && i.Troca.DataHora < fimUtc)
                    .SumAsync(i => (decimal?)(i.CustoUnitario * i.Quantidade)) ?? 0);
            v["perdas"] = await db.Movimentacoes.AsNoTracking()
                .Where(m => (m.Tipo == TipoMovimentacao.Perda || m.Tipo == TipoMovimentacao.Ajuste) && m.DataHora >= ini && m.DataHora < fimUtc)
                .SumAsync(m => (decimal?)(m.Quantidade * (m.CustoUnitario ?? m.Produto!.PrecoCusto))) ?? 0;

            // Categorias (competência)
            var porCategoria = await PorCategoriaAsync(inicio, fim);
            foreach (var (id, valor) in porCategoria) v["categoria:" + id] = valor;
            decimal Grupo(GrupoFinanceiro g) => porGrupo.GetValueOrDefault(g, []).Sum(c => porCategoria.GetValueOrDefault(c.Id));

            v["impostos"] = aliquota > 0 ? -Math.Round(receita * aliquota / 100m, 2) : Grupo(GrupoFinanceiro.ImpostosVendas);
            v["receita-liquida"] = receita + v["impostos"];
            v["lucro-bruto"] = v["receita-liquida"] + v["cmv"] + v["perdas"];
            foreach (var g in DespesasOperacionais.Append(GrupoFinanceiro.OutrasReceitas).Append(GrupoFinanceiro.Financeiras).Append(GrupoFinanceiro.ForaDoResultado))
                v["grupo:" + g] = Grupo(g);
            v["resultado-operacional"] = v["lucro-bruto"] + DespesasOperacionais.Sum(g => v["grupo:" + g]);
            v["lucro-liquido"] = v["resultado-operacional"] + v["grupo:" + GrupoFinanceiro.OutrasReceitas] + v["grupo:" + GrupoFinanceiro.Financeiras];

            // Informativos (regime de caixa, para conferência)
            var das = porGrupo.GetValueOrDefault(GrupoFinanceiro.ImpostosVendas, []).Select(c => c.Id).ToList();
            var compras = porGrupo.GetValueOrDefault(GrupoFinanceiro.ComprasMercadorias, []).Select(c => c.Id).ToList();
            var noMes = db.MovimentosFinanceiros.AsNoTracking().Where(m => m.Data >= inicio && m.Data <= fim && m.Tipo == TipoMovimentoFinanceiro.Saida);
            var dasPago = await noMes.Where(m => das.Contains(m.CategoriaFinanceiraId!.Value)).SumAsync(m => (decimal?)m.Valor) ?? 0;
            var comprasPagas = await noMes.Where(m => compras.Contains(m.CategoriaFinanceiraId!.Value)).SumAsync(m => (decimal?)m.Valor) ?? 0;

            mesesDre.Add(new MesDre($"{inicio:yyyy-MM}", $"{NomesMeses[inicio.Month - 1]}/{inicio.Year}", inicio, fim,
                fim >= hoje, dasPago, comprasPagas));
            col.Add(v);
        }

        // Monta as linhas na ordem da DRE (categorias só aparecem se tiveram valor em algum mês).
        var linhas = new List<LinhaDre>();
        List<decimal> Valores(string chave) => col.Select(v => v.GetValueOrDefault(chave)).ToList();
        void Linha(string chave, string nome, int nivel, string tipo, bool detalhavel = true, bool sempre = true)
        {
            var valores = Valores(chave);
            if (sempre || valores.Any(x => x != 0)) linhas.Add(new LinhaDre(chave, nome, nivel, tipo, valores, detalhavel));
        }
        void GrupoComCategorias(GrupoFinanceiro g, string tipo)
        {
            Linha("grupo:" + g, Financeiro.NomeGrupo(g), 1, tipo);
            foreach (var c in porGrupo.GetValueOrDefault(g, []))
                Linha("categoria:" + c.Id, c.Nome, 2, tipo, sempre: false);
        }

        Linha("receita-bruta", "Receita bruta de vendas", 0, "receita");
        Linha("descontos", "(−) Descontos concedidos", 1, "deducao", false, sempre: false);
        Linha("devolucoes", "(−) Devoluções (trocas)", 1, "deducao", sempre: false);
        Linha("impostos", aliquota > 0 ? $"(−) Impostos sobre vendas (Simples {aliquota.ToString("0.##", PtBr)}%)" : "(−) Impostos sobre vendas (DAS lançados)", 1, "deducao");
        Linha("receita-liquida", "= Receita líquida", 0, "subtotal", false);
        Linha("cmv", "(−) Custo das mercadorias vendidas (CMV)", 1, "deducao");
        Linha("perdas", "(−) Perdas e ajustes de estoque", 1, "deducao");
        Linha("lucro-bruto", "= Lucro bruto", 0, "subtotal", false);
        foreach (var g in DespesasOperacionais) GrupoComCategorias(g, "deducao");
        Linha("resultado-operacional", "= Resultado operacional", 0, "subtotal", false);
        GrupoComCategorias(GrupoFinanceiro.OutrasReceitas, "receita");
        GrupoComCategorias(GrupoFinanceiro.Financeiras, "deducao");
        Linha("lucro-liquido", "= LUCRO LÍQUIDO", 0, "resultado", false);

        var fora = porGrupo.GetValueOrDefault(GrupoFinanceiro.ForaDoResultado, [])
            .Select(c => new LinhaDre("categoria:" + c.Id, c.Nome, 2, c.Natureza == NaturezaFinanceira.Entrada ? "receita" : "deducao", Valores("categoria:" + c.Id), true))
            .Where(l => l.Valores.Any(x => x != 0)).ToList();

        return new DreResponse(mesesDre, linhas, fora, aliquota, aliquota > 0);
    }

    // Saldo de vale-troca que venceu no mês sem ser usado (a loja fica com ele). Só conta depois de vencido de fato.
    private async Task<decimal> ValesVencidosAsync(DateOnly inicio, DateOnly fim, DateOnly hoje) =>
        await db.ValesTroca.AsNoTracking().Where(v => v.Saldo > 0 && v.Validade >= inicio && v.Validade <= fim && v.Validade < hoje)
            .SumAsync(v => (decimal?)v.Saldo) ?? 0;

    // Valor de cada categoria no mês, por competência (com sinal: entrada +, saída −).
    private async Task<Dictionary<int, decimal>> PorCategoriaAsync(DateOnly inicio, DateOnly fim)
    {
        var resultado = new Dictionary<int, decimal>();
        void Somar(int id, decimal valor) => resultado[id] = resultado.GetValueOrDefault(id) + valor;

        // (a) Contas a pagar pelo vencimento (pagas ou não): o aluguel de outubro é despesa de outubro.
        foreach (var c in await db.ContasPagar.AsNoTracking()
                     .Where(c => c.CategoriaFinanceiraId != null && c.Vencimento >= inicio && c.Vencimento <= fim)
                     .GroupBy(c => c.CategoriaFinanceiraId!.Value).Select(g => new { g.Key, Valor = g.Sum(c => c.Valor) }).ToListAsync())
            Somar(c.Key, -c.Valor);

        // (b) Livro: avulsos, caixa (quebra/sobra, taxa do PIX) e a diferença dos pagamentos (juros/desconto obtido).
        //     Fora: o valor principal das contas (já está em "a"), vendas (vêm das Vendas) e depósitos da maquininha
        //     (a taxa deles entra em "c", pela data da venda).
        var vendasPdv = await Financeiro.CategoriaAsync(db, ChavesFinanceiras.VendasPdv);
        foreach (var m in await LivroDaDre(inicio, fim, vendasPdv)
                     .GroupBy(m => new { Id = m.CategoriaFinanceiraId!.Value, m.Tipo })
                     .Select(g => new { g.Key.Id, g.Key.Tipo, Valor = g.Sum(m => m.Valor) }).ToListAsync())
            Somar(m.Id, m.Tipo == TipoMovimentoFinanceiro.Entrada ? m.Valor : -m.Valor);

        // (c) Taxas da maquininha pela data da VENDA.
        var taxa = await db.Recebiveis.AsNoTracking().Where(r => r.DataVenda >= inicio && r.DataVenda <= fim).SumAsync(r => (decimal?)r.Taxa) ?? 0;
        if (taxa != 0) Somar(await Financeiro.CategoriaAsync(db, ChavesFinanceiras.TaxasCartao), -taxa);

        // (d) Fiado perdoado / calote: a receita contou no dia da venda; a baixa vira despesa no dia da baixa.
        var baixas = await db.LancamentosFiado.AsNoTracking()
            .Where(l => l.Tipo == TipoLancamentoFiado.Baixa && l.DataHora >= Relogio.InicioDoDiaUtc(inicio) && l.DataHora < Relogio.InicioDoDiaUtc(fim.AddDays(1)))
            .SumAsync(l => (decimal?)l.Valor) ?? 0;
        if (baixas != 0) Somar(await Financeiro.CategoriaAsync(db, ChavesFinanceiras.Inadimplencia), -baixas);
        return resultado;
    }

    private IQueryable<MovimentoFinanceiro> LivroDaDre(DateOnly inicio, DateOnly fim, int vendasPdv) =>
        db.MovimentosFinanceiros.AsNoTracking().Where(m =>
            m.Data >= inicio && m.Data <= fim && m.CategoriaFinanceiraId != null && m.CategoriaFinanceiraId != vendasPdv
            && m.CategoriaFinanceira!.Grupo != GrupoFinanceiro.ReceitaVendas // vendas e fiado recebido: a receita vem das Vendas
            && m.Origem != OrigemMovimentoFinanceiro.Recebimento
            && (m.ContaPagarId == null || m.CategoriaFinanceira!.Chave == ChavesFinanceiras.Juros
                || m.CategoriaFinanceira.Chave == ChavesFinanceiras.DescontosObtidos));

    // ================================================================ DETALHE (clicou numa linha)

    public async Task<DetalheDreResponse> DetalharAsync(DateOnly mes, string linha)
    {
        var inicio = new DateOnly(mes.Year, mes.Month, 1);
        var fim = inicio.AddMonths(1).AddDays(-1);
        var (ini, fimUtc) = (Relogio.InicioDoDiaUtc(inicio), Relogio.InicioDoDiaUtc(fim.AddDays(1)));
        var rotulo = $"{NomesMeses[inicio.Month - 1]}/{inicio.Year}";

        switch (linha)
        {
            case "receita-bruta":
            {
                var dias = (await db.Vendas.AsNoTracking()
                        .Where(v => v.Status == StatusVenda.Concluida && v.DataHora >= ini && v.DataHora < fimUtc)
                        .Select(v => new { v.DataHora, v.Subtotal }).ToListAsync())
                    .GroupBy(v => Relogio.DiaLocal(v.DataHora)).OrderBy(g => g.Key)
                    .Select(g => new ItemDetalheDre(g.Key, $"{g.Count()} venda(s)", $"ticket médio {R(g.Sum(v => v.Subtotal) / g.Count())}", g.Sum(v => v.Subtotal)))
                    .ToList();
                return new($"Receita bruta · {rotulo}", "Vendas concluídas, por dia (canceladas não entram).", dias);
            }
            case "devolucoes":
            {
                var itens = (await db.Trocas.AsNoTracking().Where(t => t.DataHora >= ini && t.DataHora < fimUtc).OrderBy(t => t.DataHora)
                        .Select(t => new { t.DataHora, t.VendaId, t.Motivo, t.Valor, Itens = t.Itens.Select(i => i.Descricao).ToList() }).ToListAsync())
                    .Select(t => new ItemDetalheDre(Relogio.DiaLocal(t.DataHora), $"Troca da venda #{t.VendaId}: {string.Join(", ", t.Itens)}",
                        $"motivo: {t.Motivo.ToLowerInvariant()} · virou vale-troca", -t.Valor)).ToList();
                itens.AddRange(await db.ValesTroca.AsNoTracking()
                    .Where(v => v.Saldo > 0 && v.Validade >= inicio && v.Validade <= fim && v.Validade < Relogio.Hoje)
                    .Select(v => new ItemDetalheDre(v.Validade, $"Vale-troca {v.Codigo} venceu sem uso", "a loja fica com o saldo", v.Saldo)).ToListAsync());
                return new($"Devoluções (trocas) · {rotulo}",
                    "O crédito de cada troca sai da receita (o custo dos itens volta ao CMV). Vale-troca vencido com saldo volta como receita.",
                    itens.OrderBy(i => i.Data).ToList());
            }
            case "impostos":
            {
                var aliquota = await db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.AliquotaSimples).FirstAsync();
                var receita = await db.Vendas.Where(v => v.Status == StatusVenda.Concluida && v.DataHora >= ini && v.DataHora < fimUtc)
                    .SumAsync(v => (decimal?)v.Total) ?? 0;
                receita += -(await db.Trocas.Where(t => t.DataHora >= ini && t.DataHora < fimUtc).SumAsync(t => (decimal?)t.Valor) ?? 0)
                           + await ValesVencidosAsync(inicio, fim, Relogio.Hoje);
                var impostos =await db.CategoriasFinanceiras.Where(c => c.Grupo == GrupoFinanceiro.ImpostosVendas).Select(c => c.Id).ToListAsync();
                var das = await db.ContasPagar.AsNoTracking()
                    .Where(c => c.CategoriaFinanceiraId != null && impostos.Contains(c.CategoriaFinanceiraId.Value)
                                && c.Vencimento >= inicio.AddMonths(-1) && c.Vencimento <= fim.AddMonths(1))
                    .OrderBy(c => c.Vencimento)
                    .Select(c => new ItemDetalheDre(c.Vencimento, c.Descricao, c.PagaEm != null ? "pago" : "em aberto", -c.Valor)).ToListAsync();
                var obs = aliquota > 0
                    ? $"Competência: receita de {R(receita)} × alíquota efetiva de {aliquota.ToString("0.##", PtBr)}% = {R(Math.Round(receita * aliquota / 100m, 2))}. Abaixo, os DAS lançados (vencidos no mês anterior, neste e no seguinte), para conferir com o contador."
                    : "Alíquota do Simples ainda não informada: a DRE usa os DAS que vencem no mês. Informe a alíquota efetiva (o contador sabe) para o imposto ficar no mês certo.";
                return new($"Impostos sobre vendas · {rotulo}", obs, das);
            }
            case "cmv":
            {
                var itens = await db.ItensVenda.AsNoTracking()
                    .Where(i => i.Venda!.Status == StatusVenda.Concluida && i.Venda.DataHora >= ini && i.Venda.DataHora < fimUtc)
                    .GroupBy(i => new { i.Descricao, i.Unidade })
                    .Select(g => new { Produto = g.Key.Descricao, g.Key.Unidade, Qtd = g.Sum(i => i.Quantidade), Custo = g.Sum(i => i.CustoUnitario * i.Quantidade), Receita = g.Sum(i => i.Total) })
                    .OrderByDescending(x => x.Custo).Take(40).ToListAsync();
                return new($"Custo das mercadorias vendidas · {rotulo}",
                    "Os 40 produtos que mais pesaram no custo. O custo de cada item fica gravado no momento da venda.",
                    itens.Select(x => new ItemDetalheDre(null, x.Produto,
                        $"{x.Qtd.ToString("0.###", PtBr)} {x.Unidade.ToLowerInvariant()} vendidos · margem {(x.Receita == 0 ? 0 : (1 - x.Custo / x.Receita) * 100).ToString("0.0", PtBr)}%", -x.Custo)).ToList());
            }
            case "perdas":
            {
                var itens = await db.Movimentacoes.AsNoTracking()
                    .Where(m => (m.Tipo == TipoMovimentacao.Perda || m.Tipo == TipoMovimentacao.Ajuste) && m.DataHora >= ini && m.DataHora < fimUtc)
                    .OrderBy(m => m.DataHora)
                    .Select(m => new { m.DataHora, Produto = m.Produto!.Nome, m.Tipo, m.Motivo, m.Quantidade, Custo = m.Quantidade * (m.CustoUnitario ?? m.Produto.PrecoCusto) })
                    .ToListAsync();
                return new($"Perdas e ajustes de estoque · {rotulo}", "Pelo custo. Ajuste positivo (sobrou na contagem) diminui a perda.",
                    itens.Select(x => new ItemDetalheDre(Relogio.DiaLocal(x.DataHora), x.Produto,
                        $"{(x.Tipo == TipoMovimentacao.Perda ? x.Motivo ?? "perda" : "ajuste de inventário")} · {x.Quantidade.ToString("+0.###;-0.###", PtBr)}", x.Custo)).ToList());
            }
        }

        // Grupo ou categoria
        List<int> ids;
        string titulo;
        if (linha.StartsWith("grupo:") && Enum.TryParse<GrupoFinanceiro>(linha[6..], out var grupo))
        {
            ids = await db.CategoriasFinanceiras.Where(c => c.Grupo == grupo).Select(c => c.Id).ToListAsync();
            titulo = Financeiro.NomeGrupo(grupo);
        }
        else if (linha.StartsWith("categoria:") && int.TryParse(linha[10..], out var id))
        {
            ids = [id];
            titulo = await db.CategoriasFinanceiras.Where(c => c.Id == id).Select(c => c.Nome).FirstOrDefaultAsync() ?? "Categoria";
        }
        else throw new ArgumentException("Linha da DRE desconhecida.");

        var lista = new List<ItemDetalheDre>();
        lista.AddRange(await db.ContasPagar.AsNoTracking()
            .Where(c => c.CategoriaFinanceiraId != null && ids.Contains(c.CategoriaFinanceiraId.Value) && c.Vencimento >= inicio && c.Vencimento <= fim)
            .Select(c => new ItemDetalheDre(c.Vencimento, c.Descricao, "conta a pagar · " + (c.PagaEm != null ? "paga" : "em aberto"), -c.Valor)).ToListAsync());
        var vendasPdv = await Financeiro.CategoriaAsync(db, ChavesFinanceiras.VendasPdv);
        lista.AddRange((await LivroDaDre(inicio, fim, vendasPdv).Where(m => ids.Contains(m.CategoriaFinanceiraId!.Value))
                .Select(m => new { m.Data, m.Descricao, m.Origem, m.Tipo, m.Valor }).ToListAsync())
            .Select(m => new ItemDetalheDre(m.Data, m.Descricao, NomeOrigem(m.Origem), m.Tipo == TipoMovimentoFinanceiro.Entrada ? m.Valor : -m.Valor)));
        var inadimplencia = await Financeiro.CategoriaAsync(db, ChavesFinanceiras.Inadimplencia);
        if (ids.Contains(inadimplencia))
            lista.AddRange((await db.LancamentosFiado.AsNoTracking()
                    .Where(l => l.Tipo == TipoLancamentoFiado.Baixa && l.DataHora >= ini && l.DataHora < fimUtc)
                    .Select(l => new { l.DataHora, Cliente = l.Cliente!.Nome, l.Observacao, l.Valor }).ToListAsync())
                .Select(l => new ItemDetalheDre(Relogio.DiaLocal(l.DataHora), $"Baixa do fiado de {l.Cliente}", l.Observacao, -l.Valor)));
        var taxas = await Financeiro.CategoriaAsync(db, ChavesFinanceiras.TaxasCartao);
        if (ids.Contains(taxas))
            lista.AddRange((await db.Recebiveis.AsNoTracking().Where(r => r.DataVenda >= inicio && r.DataVenda <= fim)
                    .GroupBy(r => new { r.DataVenda, r.Forma })
                    .Select(g => new { g.Key.DataVenda, g.Key.Forma, Bruto = g.Sum(r => r.ValorBruto), Taxa = g.Sum(r => r.Taxa) }).ToListAsync())
                .Select(r => new ItemDetalheDre(r.DataVenda, $"Taxa do {FinanceiroCaixa.NomeForma(r.Forma)} sobre {R(r.Bruto)} vendidos", "maquininha", -r.Taxa)));
        return new($"{titulo} · {rotulo}", "Contas a pagar pelo vencimento (pagas ou não), lançamentos pela data e taxas da maquininha pela data da venda.",
            lista.OrderBy(i => i.Data).ThenBy(i => i.Descricao).ToList());
    }

    private static string NomeOrigem(OrigemMovimentoFinanceiro o) => o switch
    {
        OrigemMovimentoFinanceiro.Caixa => "fechamento do caixa",
        OrigemMovimentoFinanceiro.ContaPagar => "juros/desconto no pagamento",
        OrigemMovimentoFinanceiro.Importacao => "importado",
        _ => "lançamento",
    };

    private static int Ordem(string codigo) => int.TryParse(codigo.Split('.').Last(), out var n) ? n : 0;
    private static string R(decimal v) => v.ToString("C", PtBr);
}
