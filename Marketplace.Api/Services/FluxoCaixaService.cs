using System.Globalization;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Fluxo de caixa: "vou ter dinheiro para pagar o fornecedor dia 20?"
//  REALIZADO = o livro (MovimentosFinanceiros), somando todas as contas, sem transferências (o dinheiro só mudou de lugar).
//  PREVISTO  = saldo de hoje + recebíveis da maquininha + vendas ESTIMADAS − contas a pagar em aberto, dia a dia.
// As vendas estimadas usam a média das últimas 4 semanas do MESMO dia da semana (domingo fechado = média zero, sozinho),
// e cada forma cai quando cai de verdade: dinheiro e PIX no dia, cartões pela regra da maquininha (já sem a taxa).
public class FluxoCaixaService(AppDbContext db)
{
    private const int SemanasDaMedia = 4;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] Meses = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    // ================================================================ REALIZADO

    public async Task<FluxoRealizadoResponse> RealizadoAsync(DateOnly de, DateOnly ate, string agrupar)
    {
        if (de > ate) (de, ate) = (ate, de);
        var saldoInicial = (await Financeiro.SaldosAsync(db, de.AddDays(-1))).Values.Sum();

        // Só o que conta no saldo: movimentos a partir da implantação de cada conta (antes disso é histórico de consulta).
        var porDia = await db.MovimentosFinanceiros.AsNoTracking()
            .Where(m => m.TransferenciaId == null && m.Data >= de && m.Data <= ate && m.Data >= m.ContaFinanceira!.SaldoInicialEm)
            .GroupBy(m => new { m.Data, m.Tipo })
            .Select(g => new { g.Key.Data, g.Key.Tipo, Valor = g.Sum(m => m.Valor) })
            .ToListAsync();
        var implantacoes = await db.ContasFinanceiras.AsNoTracking()
            .Where(c => c.SaldoInicialEm >= de && c.SaldoInicialEm <= ate && c.SaldoInicial != 0)
            .GroupBy(c => c.SaldoInicialEm).Select(g => new { Data = g.Key, Valor = g.Sum(c => c.SaldoInicial) })
            .ToDictionaryAsync(x => x.Data, x => x.Valor);

        var periodos = new List<PeriodoFluxo>();
        var saldo = saldoInicial;
        for (var inicio = de; inicio <= ate;)
        {
            var fim = agrupar switch
            {
                "semana" => Min(inicio.AddDays(6 - ((int)inicio.DayOfWeek + 6) % 7), ate), // até domingo
                "mes" => Min(new DateOnly(inicio.Year, inicio.Month, 1).AddMonths(1).AddDays(-1), ate),
                _ => inicio,
            };
            var dentro = porDia.Where(x => x.Data >= inicio && x.Data <= fim).ToList();
            var entradas = dentro.Where(x => x.Tipo == TipoMovimentoFinanceiro.Entrada).Sum(x => x.Valor);
            var saidas = dentro.Where(x => x.Tipo == TipoMovimentoFinanceiro.Saida).Sum(x => x.Valor);
            var implantacao = implantacoes.Where(x => x.Key >= inicio && x.Key <= fim).Sum(x => x.Value);
            saldo += implantacao + entradas - saidas;
            var rotulo = agrupar switch
            {
                "semana" => $"{inicio:dd/MM}–{fim:dd/MM}",
                "mes" => $"{Meses[inicio.Month - 1]}/{inicio.Year}",
                _ => $"{inicio:dd/MM}",
            };
            periodos.Add(new PeriodoFluxo(inicio, fim, rotulo, entradas, saidas, implantacao, saldo));
            inicio = fim.AddDays(1);
        }
        return new FluxoRealizadoResponse(de, ate, agrupar, saldoInicial, periodos.Sum(p => p.Entradas), periodos.Sum(p => p.Saidas), saldo, periodos);
    }

    // ================================================================ PREVISTO

    public async Task<FluxoPrevistoResponse> PrevistoAsync(int dias, bool comEstimativa)
    {
        dias = Math.Clamp(dias, 7, 120);
        var hoje = Relogio.Hoje;
        var fim = hoje.AddDays(dias - 1);
        var saldoHoje = (await Financeiro.SaldosAsync(db)).Values.Sum();
        var saldoMinimo = await db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.SaldoMinimoCaixa).FirstAsync();

        var itens = new Dictionary<DateOnly, List<ItemFluxo>>();
        void Somar(DateOnly data, ItemFluxo item)
        {
            if (data < hoje) data = hoje; // atrasado = conta para hoje
            if (data > fim) return;
            if (!itens.TryGetValue(data, out var lista)) itens[data] = lista = [];
            lista.Add(item);
        }

        // Contas a pagar em aberto (as vencidas pesam HOJE, em destaque).
        var contas = await db.ContasPagar.AsNoTracking().Where(c => c.PagaEm == null && c.Vencimento <= fim)
            .OrderBy(c => c.Vencimento).ThenByDescending(c => c.Valor)
            .Select(c => new { c.Id, c.Descricao, c.Vencimento, c.Valor, Fornecedor = c.Fornecedor!.Nome }).ToListAsync();
        foreach (var c in contas)
            Somar(c.Vencimento, new ItemFluxo("conta", c.Vencimento < hoje ? $"{c.Descricao} (venceu {c.Vencimento:dd/MM})" : c.Descricao,
                -c.Valor, c.Id, c.Vencimento < hoje));

        // Recebíveis da maquininha (líquidos), agrupados por dia e forma — como o depósito aparece no banco.
        var recebiveis = await db.Recebiveis.AsNoTracking().Where(r => r.Status == StatusRecebivel.Previsto && r.Previsao <= fim)
            .GroupBy(r => new { r.Previsao, r.Forma })
            .Select(g => new { g.Key.Previsao, g.Key.Forma, Liquido = g.Sum(r => r.ValorLiquido), Qtd = g.Count() }).ToListAsync();
        foreach (var r in recebiveis)
            Somar(r.Previsao, new ItemFluxo("recebivel", $"Maquininha · {FinanceiroCaixa.NomeForma(r.Forma)} ({r.Qtd} caixa(s))", r.Liquido, null, false));

        var diasHistorico = 0;
        if (comEstimativa)
            diasHistorico = await EstimarVendasAsync(hoje, fim, Somar);

        // Dia a dia.
        var lista = new List<DiaFluxo>();
        var saldo = saldoHoje;
        DateOnly? primeiroNegativo = null, primeiroAbaixo = null;
        var (menor, menorEm) = (decimal.MaxValue, hoje);
        for (var d = hoje; d <= fim; d = d.AddDays(1))
        {
            var doDia = itens.GetValueOrDefault(d) ?? [];
            var recebe = doDia.Where(i => i.Tipo is "recebivel").Sum(i => i.Valor);
            var estimado = doDia.Where(i => i.Tipo is "estimativa" or "caixa").Sum(i => i.Valor);
            var sai = -doDia.Where(i => i.Tipo == "conta").Sum(i => i.Valor);
            saldo += recebe + estimado - sai;
            var situacao = saldo < 0 ? "negativo" : saldo < saldoMinimo ? "minimo" : "ok";
            if (saldo < 0) primeiroNegativo ??= d;
            if (saldo < saldoMinimo) primeiroAbaixo ??= d;
            if (saldo < menor) (menor, menorEm) = (saldo, d);
            lista.Add(new DiaFluxo(d, recebe, estimado, sai, saldo,
                situacao, doDia.OrderBy(i => i.Tipo == "conta" ? 1 : 0).ThenBy(i => i.Valor).ToList()));
        }

        return new FluxoPrevistoResponse(hoje, dias, comEstimativa, saldoHoje, saldoMinimo,
            lista.Sum(x => x.Recebiveis), lista.Sum(x => x.Estimado), lista.Sum(x => x.Saidas), saldo,
            menor, menorEm, primeiroNegativo, primeiroAbaixo, Alerta(lista, primeiroNegativo, primeiroAbaixo, saldoMinimo),
            diasHistorico, lista);
    }

    // Vendas estimadas por forma: média das últimas 4 semanas do mesmo dia da semana. Hoje = o que os caixas abertos já
    // venderam + a parte do dia que ainda vai acontecer. Devolve quantos dias de histórico havia (cliente novo tem menos).
    private async Task<int> EstimarVendasAsync(DateOnly hoje, DateOnly fim, Action<DateOnly, ItemFluxo> somar)
    {
        var inicioHist = hoje.AddDays(-7 * SemanasDaMedia);
        var vendas = await db.Vendas.AsNoTracking()
            .Where(v => v.Status == StatusVenda.Concluida && v.DataHora >= Relogio.InicioDoDiaUtc(inicioHist))
            .Select(v => new { v.DataHora, v.Troco, v.SessaoCaixa!.Status, Pagamentos = v.Pagamentos.Select(p => new { p.Forma, p.Valor }) })
            .ToListAsync();
        if (vendas.Count == 0) return 0;

        var configuracoes = await db.ConfiguracoesRecebimento.AsNoTracking().ToDictionaryAsync(c => c.Forma);
        var porDiaForma = vendas
            .SelectMany(v => v.Pagamentos.Select(p => new
            {
                Dia = Relogio.DiaLocal(v.DataHora), Hora = TimeZoneInfo.ConvertTime(v.DataHora, Relogio.Fuso).TimeOfDay, p.Forma,
                Valor = p.Forma == FormaPagamento.Dinheiro ? p.Valor - v.Troco : p.Valor, // dinheiro: o troco saiu da mesma gaveta
                Aberto = v.Status == StatusSessao.Aberta,
            })).ToList();

        // Média por (dia da semana, forma), só com os dias desde a primeira venda (cliente novo: menos semanas).
        var primeiroDia = porDiaForma.Min(x => x.Dia);
        var historico = porDiaForma.Where(x => x.Dia < hoje).ToList();
        var diasHistorico = Math.Max(0, hoje.DayNumber - Max(primeiroDia, inicioHist).DayNumber);
        if (diasHistorico == 0) return 0;
        decimal Media(DayOfWeek semana, FormaPagamento forma)
        {
            var ocorrencias = Enumerable.Range(1, diasHistorico).Select(i => hoje.AddDays(-i)).Count(d => d.DayOfWeek == semana);
            return ocorrencias == 0 ? 0 : historico.Where(x => x.Dia.DayOfWeek == semana && x.Forma == forma).Sum(x => x.Valor) / ocorrencias;
        }

        var formas = Enum.GetValues<FormaPagamento>();
        // Hoje: caixas ABERTOS (vendido de verdade, ainda fora do livro) + a parte do dia que AINDA VAI acontecer:
        // a fração das vendas deste dia da semana que costuma vir depois deste horário (pelo histórico da própria loja —
        // funciona para quem fecha às 19h, às 22h ou abre domingo). Depois do fechamento, a fração é zero.
        var hojeAberto = porDiaForma.Where(x => x.Dia == hoje && x.Aberto).GroupBy(x => x.Forma).ToDictionary(g => g.Key, g => g.Sum(x => x.Valor));
        var agora = Relogio.Agora.TimeOfDay;
        var mesmoDia = historico.Where(x => x.Dia.DayOfWeek == hoje.DayOfWeek).ToList();
        var totalMesmoDia = mesmoDia.Sum(x => x.Valor);
        var fracaoRestante = totalMesmoDia == 0 ? 0 : mesmoDia.Where(x => x.Hora > agora).Sum(x => x.Valor) / totalMesmoDia;

        for (var d = hoje; d <= fim; d = d.AddDays(1))
        {
            foreach (var forma in formas)
            {
                decimal valor; string tipo, texto;
                if (d == hoje)
                {
                    var aberto = hojeAberto.GetValueOrDefault(forma);
                    if (aberto > 0) Lancar(d, forma, aberto, "caixa", "Caixa aberto agora");
                    valor = Media(d.DayOfWeek, forma) * fracaoRestante;
                    (tipo, texto) = ("estimativa", "Vendas estimadas (resto do dia)");
                }
                else
                {
                    valor = Media(d.DayOfWeek, forma);
                    (tipo, texto) = ("estimativa", "Vendas estimadas");
                }
                if (valor > 0) Lancar(d, forma, valor, tipo, texto);
            }
        }
        return diasHistorico;

        // Cada forma cai quando cai de verdade.
        void Lancar(DateOnly diaVenda, FormaPagamento forma, decimal valor, string tipo, string texto)
        {
            if (forma == FormaPagamento.Dinheiro)
            {
                somar(diaVenda, new ItemFluxo(tipo, $"{texto} · dinheiro", Math.Round(valor, 2), null, false));
                return;
            }
            if (!configuracoes.TryGetValue(forma, out var cfg)) return;
            var cai = CalendarioBancario.Previsao(diaVenda, cfg.PrazoDias, cfg.DiasUteis);
            var liquido = Math.Round(valor * (1 - cfg.TaxaPercentual / 100m), 2);
            var quando = cai == diaVenda ? "" : $" de {diaVenda:dd/MM}";
            somar(cai, new ItemFluxo(tipo, $"{texto}{quando} · {FinanceiroCaixa.NomeForma(forma)} (sem a taxa)", liquido, null, false));
        }
    }

    // O aviso em português: quando falta dinheiro e o que mais pesa até lá.
    private static string? Alerta(List<DiaFluxo> dias, DateOnly? negativo, DateOnly? abaixo, decimal minimo)
    {
        var dia = negativo ?? abaixo;
        if (dia is null) return null;
        var d = dias.First(x => x.Data == dia);
        var pesam = dias.Where(x => x.Data <= dia).SelectMany(x => x.Itens.Where(i => i.Tipo == "conta").Select(i => (x.Data, i)))
            .OrderBy(x => x.i.Valor).Take(3)
            .Select(x => $"{x.i.Descricao.Split(" (venceu")[0]} ({R(-x.i.Valor)}, {x.Data:dd/MM})").ToList();
        var texto = negativo is not null
            ? $"Em {dia:dd/MM} o saldo projetado fica em {R(d.SaldoFinal)}: faltam {R(-d.SaldoFinal)}."
            : $"Em {dia:dd/MM} o saldo projetado ({R(d.SaldoFinal)}) fica abaixo do mínimo de segurança ({R(minimo)}).";
        if (pesam.Count > 0) texto += " Pesam até lá: " + string.Join("; ", pesam) + ".";
        return texto + " Dá para negociar prazo, adiar uma compra ou antecipar recebíveis.";
    }

    private static string R(decimal v) => v.ToString("C", PtBr);
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
