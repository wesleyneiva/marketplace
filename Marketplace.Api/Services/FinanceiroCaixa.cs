using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;
using static Marketplace.Api.Models.ChavesFinanceiras;

namespace Marketplace.Api.Services;

// O caixa do PDV dentro do Financeiro. NÃO lança venda por venda (seriam milhares de linhas): lança no
// FECHAMENTO de cada caixa, uma linha por forma — como o extrato do banco mostra e como os ERPs grandes fazem.
//   Dinheiro (já sem o troco) → entra no "Caixa da loja"
//   PIX (prazo 0)            → entra no banco na hora (e a taxa, se houver, sai separada)
//   Débito / Crédito         → viram RECEBÍVEIS, que a maquininha deposita no dia previsto (LiquidarAsync)
//   Sangria / suprimento     → transferências entre a gaveta e o cofre
//   Diferença do fechamento  → Quebra de caixa (faltou) ou Sobra de caixa (sobrou)
public static class FinanceiroCaixa
{
    // Prepara os lançamentos de um caixa FECHADO (quem chama grava tudo num SaveChanges só: tudo ou nada).
    // Devolve false quando não lança: já lançado, caixa aberto, ou de antes da implantação do financeiro.
    public static async Task<bool> PrepararFechamentoAsync(AppDbContext db, SessaoCaixa sessao)
    {
        if (sessao.Status != StatusSessao.Fechada || sessao.LancadaNoFinanceiroEm is not null) return false;

        var gaveta = await db.ContasFinanceiras.FirstAsync(c => c.Chave == ContaDinheiro);
        var dia = Relogio.DiaLocal(sessao.AbertaEm);
        if (dia < gaveta.SaldoInicialEm) return false; // caixa de antes da implantação: o saldo inicial já conta com ele

        var cofre = await db.ContasFinanceiras.Where(c => c.Chave == ContaCofre).Select(c => c.Id).FirstAsync();
        var vendasPdv = await Financeiro.CategoriaAsync(db, VendasPdv);
        var configuracoes = await db.ConfiguracoesRecebimento.AsNoTracking().ToDictionaryAsync(c => c.Forma);
        var operador = await db.Users.Where(u => u.Id == sessao.UsuarioId).Select(u => u.NomeCompleto).FirstOrDefaultAsync();
        var quem = $"Caixa {sessao.NumeroCaixa}{(string.IsNullOrWhiteSpace(operador) ? "" : " · " + operador)}";

        // Vendas concluídas do caixa, por forma (as canceladas não entram: o dinheiro voltou ao cliente).
        var porForma = await db.PagamentosVenda.AsNoTracking()
            .Where(p => p.Venda!.SessaoCaixaId == sessao.Id && p.Venda.Status == StatusVenda.Concluida)
            .GroupBy(p => p.Forma).Select(g => new { Forma = g.Key, Valor = g.Sum(p => p.Valor) }).ToListAsync();
        var troco = await db.Vendas.Where(v => v.SessaoCaixaId == sessao.Id && v.Status == StatusVenda.Concluida)
            .SumAsync(v => (decimal?)v.Troco) ?? 0;

        MovimentoFinanceiro Mov(TipoMovimentoFinanceiro tipo, decimal valor, int conta, int? categoria, string descricao) => new()
        {
            Data = dia, Tipo = tipo, Valor = valor, ContaFinanceiraId = conta, CategoriaFinanceiraId = categoria,
            Descricao = Cortar(descricao), Origem = OrigemMovimentoFinanceiro.Caixa, SessaoCaixaId = sessao.Id,
        };
        var movimentos = new List<MovimentoFinanceiro>();

        // Fiado pago no balcão: entra como o dinheiro das vendas (mesmas regras por forma), mas na categoria
        // "Recebimento de fiado" — a receita dele já contou no dia da venda (DRE por competência).
        var fiadoRecebido = await db.LancamentosFiado.AsNoTracking()
            .Where(l => l.SessaoCaixaId == sessao.Id && l.Tipo == TipoLancamentoFiado.Pagamento && l.Forma != null)
            .GroupBy(l => l.Forma!.Value).Select(g => new { Forma = g.Key, Valor = g.Sum(l => l.Valor) }).ToListAsync();
        var recebimentoFiado = fiadoRecebido.Count > 0 ? await Financeiro.CategoriaAsync(db, RecebimentoFiado) : 0;
        foreach (var f in fiadoRecebido.Where(f => f.Forma is FormaPagamento.Dinheiro or FormaPagamento.Pix && f.Valor > 0))
        {
            var conta = f.Forma == FormaPagamento.Dinheiro ? gaveta.Id
                : configuracoes.GetValueOrDefault(FormaPagamento.Pix)?.ContaFinanceiraId ?? gaveta.Id;
            movimentos.Add(Mov(TipoMovimentoFinanceiro.Entrada, f.Valor, conta, recebimentoFiado, $"Fiado recebido no {NomeForma(f.Forma)} · {quem}"));
        }
        // Cartões: somam com as vendas no MESMO recebível do caixa (a maquininha deposita tudo junto).
        var cartoesFiado = fiadoRecebido.Where(f => f.Forma is FormaPagamento.Debito or FormaPagamento.Credito)
            .ToDictionary(f => f.Forma, f => f.Valor);
        var vendasPorForma = porForma.Where(f => f.Forma != FormaPagamento.Fiado) // fiado não é dinheiro: vira dívida na caderneta
            .Select(f => new { f.Forma, Valor = f.Valor + cartoesFiado.GetValueOrDefault(f.Forma) })
            .Concat(cartoesFiado.Where(c => porForma.All(p => p.Forma != c.Key)).Select(c => new { Forma = c.Key, Valor = c.Value }))
            .ToList();

        foreach (var f in vendasPorForma.Where(f => f.Valor > 0))
        {
            if (f.Forma == FormaPagamento.Dinheiro)
            {
                var liquido = f.Valor - troco; // o troco saiu da mesma gaveta
                if (liquido > 0)
                    movimentos.Add(Mov(TipoMovimentoFinanceiro.Entrada, liquido, gaveta.Id, vendasPdv, $"Vendas em dinheiro · {quem}"));
                continue;
            }

            var cfg = configuracoes.GetValueOrDefault(f.Forma)
                ?? throw new InvalidOperationException($"Falta a configuração de recebimento de {f.Forma}.");
            var taxa = Dinheiro(f.Valor * cfg.TaxaPercentual / 100m);
            var previsao = CalendarioBancario.Previsao(dia, cfg.PrazoDias, cfg.DiasUteis);

            if (previsao == dia && cfg.PrazoDias == 0)
            {
                // Na hora (o PIX): entra o valor cheio e a taxa sai separada (vai para "Taxas de cartão e PIX" na DRE).
                movimentos.Add(Mov(TipoMovimentoFinanceiro.Entrada, f.Valor, cfg.ContaFinanceiraId, vendasPdv, $"Vendas no {NomeForma(f.Forma)} · {quem}"));
                if (taxa > 0)
                    movimentos.Add(Mov(TipoMovimentoFinanceiro.Saida, taxa, cfg.ContaFinanceiraId,
                        await Financeiro.CategoriaAsync(db, TaxasCartao), $"Taxa do {NomeForma(f.Forma)} ({cfg.TaxaPercentual:0.##}%) · {quem}"));
                continue;
            }

            db.Recebiveis.Add(new Recebivel
            {
                SessaoCaixaId = sessao.Id, Forma = f.Forma, DataVenda = dia, Previsao = previsao,
                ValorBruto = f.Valor, TaxaPercentual = cfg.TaxaPercentual, Taxa = taxa, ValorLiquido = f.Valor - taxa,
                ContaFinanceiraId = cfg.ContaFinanceiraId,
            });
        }

        // Sangria = gaveta → cofre; suprimento = cofre → gaveta (o dinheiro só mudou de lugar).
        var movimentosCaixa = await db.MovimentosCaixa.AsNoTracking().Where(m => m.SessaoCaixaId == sessao.Id).OrderBy(m => m.Id).ToListAsync();
        foreach (var m in movimentosCaixa)
        {
            var (de, para, nome) = m.Tipo == TipoMovimentoCaixa.Sangria ? (gaveta.Id, cofre, "Sangria") : (cofre, gaveta.Id, "Suprimento");
            var guid = Guid.NewGuid();
            var texto = $"{nome} · {quem}{(string.IsNullOrWhiteSpace(m.Motivo) ? "" : ": " + m.Motivo)}";
            var saida = Mov(TipoMovimentoFinanceiro.Saida, m.Valor, de, null, texto);
            var entrada = Mov(TipoMovimentoFinanceiro.Entrada, m.Valor, para, null, texto);
            saida.TransferenciaId = entrada.TransferenciaId = guid;
            movimentos.AddRange([saida, entrada]);
        }

        // Fechamento cego: o que o operador contou − o que deveria haver.
        if (sessao.Diferenca is decimal diferenca && diferenca != 0)
            movimentos.Add(diferenca < 0
                ? Mov(TipoMovimentoFinanceiro.Saida, -diferenca, gaveta.Id, await Financeiro.CategoriaAsync(db, QuebraCaixa), $"Quebra de caixa (faltou) · {quem}")
                : Mov(TipoMovimentoFinanceiro.Entrada, diferenca, gaveta.Id, await Financeiro.CategoriaAsync(db, SobraCaixa), $"Sobra de caixa · {quem}"));

        db.MovimentosFinanceiros.AddRange(movimentos);
        sessao.LancadaNoFinanceiroEm = DateTimeOffset.UtcNow;
        return true;
    }

    // Deixa a empresa em dia: (1) caixas fechados ainda não lançados (ex.: fechados antes desta versão, ou o
    // lançamento falhou) e (2) recebíveis que já venceram. Pode rodar quantas vezes quiser (nunca lança duas vezes).
    public static async Task<(int Caixas, int Recebiveis)> PorEmDiaAsync(AppDbContext db, CancellationToken ct = default)
    {
        var desde = await db.ContasFinanceiras.Where(c => c.Chave == ContaDinheiro).Select(c => (DateOnly?)c.SaldoInicialEm).FirstOrDefaultAsync(ct);
        if (desde is null) return (0, 0); // empresa sem financeiro (ainda não passou pela semente)

        var pendentes = await db.SessoesCaixa
            .Where(s => s.Status == StatusSessao.Fechada && s.LancadaNoFinanceiroEm == null && s.AbertaEm >= Relogio.InicioDoDiaUtc(desde.Value))
            .OrderBy(s => s.AbertaEm).ToListAsync(ct);
        var caixas = 0;
        foreach (var sessao in pendentes)
        {
            if (await PrepararFechamentoAsync(db, sessao)) caixas++;
            await db.SaveChangesAsync(ct); // um caixa por vez: se um der problema, os outros não se perdem
        }
        return (caixas, await LiquidarAsync(db, Relogio.Hoje));
    }

    // A maquininha "deposita": recebíveis com previsão até "hoje" viram movimentos no banco.
    // Um depósito por dia, forma e conta (como aparece no extrato do banco — e na conciliação OFX da Etapa 5):
    // entrada do valor BRUTO em "Vendas no PDV" + saída da TAXA em "Taxas de cartão e PIX".
    public static async Task<int> LiquidarAsync(AppDbContext db, DateOnly hoje)
    {
        var vencidos = await db.Recebiveis
            .Where(r => r.Status == StatusRecebivel.Previsto && r.Previsao <= hoje).ToListAsync();
        if (vencidos.Count == 0) return 0;

        var vendasPdv = await Financeiro.CategoriaAsync(db, VendasPdv);
        var taxas = await Financeiro.CategoriaAsync(db, TaxasCartao);
        foreach (var g in vencidos.GroupBy(r => (r.Previsao, r.Forma, r.ContaFinanceiraId)))
        {
            var guid = Guid.NewGuid();
            var dias = g.Select(r => r.DataVenda).Distinct().Order().ToList();
            var vendasDe = dias.Count == 1 ? $"{dias[0]:dd/MM}" : $"{dias[0]:dd/MM} a {dias[^1]:dd/MM}";
            var bruto = g.Sum(r => r.ValorBruto);
            var taxa = g.Sum(r => r.Taxa);
            var forma = NomeForma(g.Key.Forma);

            MovimentoFinanceiro Mov(TipoMovimentoFinanceiro tipo, decimal valor, int categoria, string descricao) => new()
            {
                Data = g.Key.Previsao, Tipo = tipo, Valor = valor, ContaFinanceiraId = g.Key.ContaFinanceiraId, CategoriaFinanceiraId = categoria,
                Descricao = Cortar(descricao), Origem = OrigemMovimentoFinanceiro.Recebimento, LiquidacaoId = guid,
            };
            db.MovimentosFinanceiros.Add(Mov(TipoMovimentoFinanceiro.Entrada, bruto, vendasPdv, $"Recebimento {forma} · vendas de {vendasDe}"));
            if (taxa > 0)
                db.MovimentosFinanceiros.Add(Mov(TipoMovimentoFinanceiro.Saida, taxa, taxas, $"Taxa {forma} · vendas de {vendasDe}"));

            foreach (var r in g)
            {
                r.Status = StatusRecebivel.Recebido;
                r.RecebidoEm = g.Key.Previsao;
                r.LiquidacaoId = guid;
            }
        }
        await db.SaveChangesAsync();
        return vencidos.Count;
    }

    public static string NomeForma(FormaPagamento f) => f switch
    {
        FormaPagamento.Pix => "PIX",
        FormaPagamento.Debito => "débito",
        FormaPagamento.Credito => "crédito",
        FormaPagamento.Fiado => "fiado",
        _ => "dinheiro",
    };

    private static decimal Dinheiro(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static string Cortar(string t) => t.Length > 200 ? t[..200] : t;
}
