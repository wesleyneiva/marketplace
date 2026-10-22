using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Recebíveis da maquininha (débito/crédito que ainda vão cair), contrato da maquininha e caixas abertos.
[ApiController]
[Route("api/financeiro")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class RecebiveisController(AppDbContext db, CaixaService caixa) : ControllerBase
{
    private static readonly FormaPagamento[] FormasEletronicas = [FormaPagamento.Pix, FormaPagamento.Debito, FormaPagamento.Credito];

    // GET /api/financeiro/recebiveis?situacao=previstos|recebidos&de=&ate=
    [HttpGet("recebiveis")]
    public async Task<List<RecebivelResponse>> Listar(string situacao = "previstos", DateOnly? de = null, DateOnly? ate = null)
    {
        var q = db.Recebiveis.AsNoTracking();
        if (situacao == "recebidos")
        {
            var fim = ate ?? Relogio.Hoje;
            var inicio = de ?? fim.AddDays(-30);
            q = q.Where(r => r.Status == StatusRecebivel.Recebido && r.RecebidoEm >= inicio && r.RecebidoEm <= fim)
                .OrderByDescending(r => r.RecebidoEm).ThenBy(r => r.Id);
        }
        else
        {
            q = q.Where(r => r.Status == StatusRecebivel.Previsto).OrderBy(r => r.Previsao).ThenBy(r => r.Id);
        }
        var lista = await q.Take(1000).Select(r => new
        {
            r.Id, r.SessaoCaixaId, r.SessaoCaixa!.NumeroCaixa, r.Forma, r.DataVenda, r.Previsao, r.ValorBruto, r.TaxaPercentual,
            r.Taxa, r.ValorLiquido, Conta = r.ContaFinanceira!.Nome, r.Status, r.RecebidoEm, r.Parcela, r.Parcelas,
        }).ToListAsync();
        return lista.Select(r => new RecebivelResponse(r.Id, r.SessaoCaixaId, r.NumeroCaixa, r.Forma.ToString(), r.DataVenda, r.Previsao,
            r.ValorBruto, r.TaxaPercentual, r.Taxa, r.ValorLiquido, r.Conta, r.Status, r.RecebidoEm, r.Parcela, r.Parcelas)).ToList();
    }

    // GET /api/financeiro/recebiveis/resumo → números do topo da aba "A receber"
    [HttpGet("recebiveis/resumo")]
    public async Task<ResumoRecebiveisResponse> Resumo()
    {
        var hoje = Relogio.Hoje;
        var previstos = await db.Recebiveis.AsNoTracking().Where(r => r.Status == StatusRecebivel.Previsto)
            .Select(r => new { r.Previsao, r.ValorLiquido }).ToListAsync();
        decimal Ate(DateOnly dia) => previstos.Where(r => r.Previsao <= dia).Sum(r => r.ValorLiquido);

        // O mês pela data da VENDA: recebíveis (débito/crédito) + o que entrou na hora (PIX) e as taxas dele.
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var doMes = db.Recebiveis.AsNoTracking().Where(r => r.DataVenda >= inicioMes);
        var brutoCartao = await doMes.SumAsync(r => (decimal?)r.ValorBruto) ?? 0;
        var taxaCartao = await doMes.SumAsync(r => (decimal?)r.Taxa) ?? 0;
        var vendasPdv = await Financeiro.CategoriaAsync(db, ChavesFinanceiras.VendasPdv);
        var taxas = await Financeiro.CategoriaAsync(db, ChavesFinanceiras.TaxasCartao);
        var naHora = db.MovimentosFinanceiros.AsNoTracking()
            .Where(m => m.Origem == OrigemMovimentoFinanceiro.Caixa && m.Data >= inicioMes);
        var brutoNaHora = await naHora.Where(m => m.CategoriaFinanceiraId == vendasPdv && m.ContaFinanceira!.Tipo != TipoContaFinanceira.Dinheiro)
            .SumAsync(m => (decimal?)m.Valor) ?? 0;
        var taxaNaHora = await naHora.Where(m => m.CategoriaFinanceiraId == taxas).SumAsync(m => (decimal?)m.Valor) ?? 0;

        var proximos = previstos.Where(r => r.Previsao <= hoje.AddDays(30)).GroupBy(r => r.Previsao < hoje ? hoje : r.Previsao)
            .OrderBy(g => g.Key).Select(g => new PrevisaoRecebimento(g.Key, g.Sum(r => r.ValorLiquido))).ToList();

        return new ResumoRecebiveisResponse(
            Ate(hoje), previstos.Where(r => r.Previsao == hoje.AddDays(1)).Sum(r => r.ValorLiquido), Ate(hoje.AddDays(7)), Ate(hoje.AddDays(30)),
            previstos.Sum(r => r.ValorLiquido), previstos.Count, brutoCartao + brutoNaHora, taxaCartao + taxaNaHora, proximos);
    }

    // POST /api/financeiro/recebiveis/atualizar → "pôr em dia agora" (o trabalho de hora em hora faz sozinho)
    [HttpPost("recebiveis/atualizar")]
    public async Task<object> Atualizar(CancellationToken ct)
    {
        var (caixas, recebiveis) = await FinanceiroCaixa.PorEmDiaAsync(db, ct);
        return new { caixas, recebiveis };
    }

    // GET /api/financeiro/recebimento/configuracao → o contrato da maquininha
    [HttpGet("recebimento/configuracao")]
    public async Task<List<ConfiguracaoRecebimentoResponse>> Configuracao()
    {
        var hoje = Relogio.Hoje;
        var lista = await db.ConfiguracoesRecebimento.AsNoTracking()
            .Select(c => new { c.Forma, c.TaxaPercentual, c.PrazoDias, c.DiasUteis, c.ContaFinanceiraId, Conta = c.ContaFinanceira!.Nome })
            .ToListAsync();
        return lista.OrderBy(c => Array.IndexOf(FormasEletronicas, c.Forma))
            .Select(c => new ConfiguracaoRecebimentoResponse(c.Forma.ToString(), c.TaxaPercentual, c.PrazoDias, c.DiasUteis, c.ContaFinanceiraId,
                c.Conta, CalendarioBancario.Previsao(hoje, c.PrazoDias, c.DiasUteis))).ToList();
    }

    // PUT /api/financeiro/recebimento/configuracao → vale para as PRÓXIMAS vendas (as já feitas guardam a taxa do dia)
    [HttpPut("recebimento/configuracao")]
    public async Task<ActionResult<List<ConfiguracaoRecebimentoResponse>>> SalvarConfiguracao(List<ConfiguracaoRecebimentoRequest> pedido)
    {
        var atuais = await db.ConfiguracoesRecebimento.ToListAsync();
        var contas = await db.ContasFinanceiras.AsNoTracking().Where(c => c.Ativa && c.Tipo != TipoContaFinanceira.Dinheiro)
            .Select(c => c.Id).ToListAsync();
        foreach (var r in pedido)
        {
            if (!Enum.TryParse<FormaPagamento>(r.Forma, out var forma) || !FormasEletronicas.Contains(forma))
                return BadRequest(new { mensagem = $"Forma inválida: {r.Forma}." });
            if (!contas.Contains(r.ContaFinanceiraId))
                return BadRequest(new { mensagem = $"{FinanceiroCaixa.NomeForma(forma)}: escolha uma conta de banco ou da maquininha (ativa)." });
            var c = atuais.FirstOrDefault(x => x.Forma == forma);
            if (c is null) db.ConfiguracoesRecebimento.Add(c = new ConfiguracaoRecebimento { Forma = forma });
            c.TaxaPercentual = Math.Round(r.TaxaPercentual, 4);
            c.PrazoDias = r.PrazoDias;
            c.DiasUteis = r.PrazoDias > 0 && r.DiasUteis;
            c.ContaFinanceiraId = r.ContaFinanceiraId;
        }
        await db.SaveChangesAsync();
        return await Configuracao();
    }

    // GET /api/financeiro/caixas-abertos → "em andamento" (entra no Financeiro no fechamento)
    [HttpGet("caixas-abertos")]
    public async Task<List<CaixaAbertoResponse>> CaixasAbertos()
    {
        var abertas = await db.SessoesCaixa.AsNoTracking().Where(s => s.Status == StatusSessao.Aberta)
            .OrderBy(s => s.NumeroCaixa).Select(s => s.Id).ToListAsync();
        var lista = new List<CaixaAbertoResponse>();
        foreach (var id in abertas)
        {
            var r = await caixa.ResumirAsync(id);
            lista.Add(new CaixaAbertoResponse(r.SessaoId, r.NumeroCaixa, r.Operador, r.AbertaEm, r.QuantidadeVendas, r.TotalVendido,
                r.DinheiroEsperado, r.PorForma.ToList()));
        }
        return lista;
    }
}
