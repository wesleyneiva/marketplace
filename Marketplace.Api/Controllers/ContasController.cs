using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Contas a pagar: as parcelas das notas de fornecedor (criadas sozinhas pela entrada da NF-e) e as lançadas à mão.
[ApiController]
[Route("api/contas")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class ContasController(AppDbContext db) : ControllerBase
{
    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // GET /api/contas?situacao=abertas|vencidas|pagas|todas
    [HttpGet]
    public async Task<List<ContaPagarResponse>> Listar(string situacao = "abertas")
    {
        var hoje = Relogio.Hoje;
        var consulta = db.ContasPagar.AsNoTracking();
        consulta = situacao switch
        {
            "vencidas" => consulta.Where(c => c.PagaEm == null && c.Vencimento < hoje),
            "pagas" => consulta.Where(c => c.PagaEm != null && c.PagaEm >= hoje.AddDays(-90)),
            "todas" => consulta.Where(c => c.PagaEm == null || c.PagaEm >= hoje.AddDays(-90)),
            _ => consulta.Where(c => c.PagaEm == null),
        };
        var ordenada = situacao == "pagas" ? consulta.OrderByDescending(c => c.PagaEm).ThenBy(c => c.Id)
            : consulta.OrderBy(c => c.Vencimento).ThenBy(c => c.Id);
        return (await Projetar(ordenada.Take(500)).ToListAsync()).Select(c => Situar(c, hoje)).ToList();
    }

    // GET /api/contas/resumo → números do topo da tela (e do dashboard)
    [HttpGet("resumo")]
    public async Task<ResumoContasResponse> Resumo()
    {
        var hoje = Relogio.Hoje;
        var semana = hoje.AddDays(7);
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var abertas = await db.ContasPagar.AsNoTracking().Where(c => c.PagaEm == null)
            .Select(c => new { c.Vencimento, c.Valor }).ToListAsync();
        var pagoNoMes = await db.ContasPagar.AsNoTracking().Where(c => c.PagaEm >= inicioMes)
            .SumAsync(c => (decimal?)(c.ValorPago ?? c.Valor)) ?? 0;

        var vencidas = abertas.Where(c => c.Vencimento < hoje).ToList();
        var deHoje = abertas.Where(c => c.Vencimento == hoje).ToList();
        var semanaQueVem = abertas.Where(c => c.Vencimento > hoje && c.Vencimento <= semana).ToList();
        return new ResumoContasResponse(vencidas.Count, vencidas.Sum(c => c.Valor), deHoje.Count, deHoje.Sum(c => c.Valor),
            semanaQueVem.Count, semanaQueVem.Sum(c => c.Valor), abertas.Count, abertas.Sum(c => c.Valor), pagoNoMes);
    }

    // POST /api/contas → conta lançada à mão (com RepetirMeses > 1 vira uma por mês: aluguel, internet...)
    [HttpPost]
    public async Task<ActionResult<List<ContaPagarResponse>>> Criar(ContaPagarRequest r)
    {
        if (await FornecedorInvalidoAsync(r.FornecedorId)) return BadRequest(new { mensagem = "Fornecedor não encontrado." });
        var contas = Enumerable.Range(0, r.RepetirMeses).Select(i => new ContaPagar
        {
            Descricao = r.RepetirMeses > 1 ? $"{r.Descricao.Trim()} ({i + 1}/{r.RepetirMeses})" : r.Descricao.Trim(),
            Documento = Limpo(r.Documento), Vencimento = r.Vencimento.AddMonths(i), Valor = r.Valor,
            FornecedorId = r.FornecedorId, Observacao = Limpo(r.Observacao), CriadaPorId = UsuarioId,
        }).ToList();
        db.ContasPagar.AddRange(contas);
        await db.SaveChangesAsync();
        var ids = contas.Select(c => c.Id).ToList();
        var hoje = Relogio.Hoje;
        return (await Projetar(db.ContasPagar.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Vencimento)).ToListAsync())
            .Select(c => Situar(c, hoje)).ToList();
    }

    // PUT /api/contas/5 → corrigir (ex.: boleto renegociado). Conta paga não muda: desfaça o pagamento antes.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ContaPagarResponse>> Editar(int id, ContaPagarRequest r)
    {
        var conta = await db.ContasPagar.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (conta.PagaEm is not null) return BadRequest(new { mensagem = "Conta já paga: desfaça o pagamento para editar." });
        if (await FornecedorInvalidoAsync(r.FornecedorId)) return BadRequest(new { mensagem = "Fornecedor não encontrado." });
        conta.Descricao = r.Descricao.Trim();
        conta.Documento = Limpo(r.Documento);
        conta.Vencimento = r.Vencimento;
        conta.Valor = r.Valor;
        conta.FornecedorId = r.FornecedorId;
        conta.Observacao = Limpo(r.Observacao);
        await db.SaveChangesAsync();
        return await UmaAsync(id);
    }

    // DELETE /api/contas/5 → só conta em aberto (lançada errada)
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var conta = await db.ContasPagar.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (conta.PagaEm is not null) return BadRequest(new { mensagem = "Conta já paga não pode ser excluída (desfaça o pagamento antes)." });
        db.ContasPagar.Remove(conta);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/contas/5/pagar
    [HttpPost("{id:int}/pagar")]
    public async Task<ActionResult<ContaPagarResponse>> Pagar(int id, PagarContaRequest r)
    {
        var conta = await db.ContasPagar.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (conta.PagaEm is not null) return BadRequest(new { mensagem = "Esta conta já foi paga." });
        if (r.PagaEm > Relogio.Hoje) return BadRequest(new { mensagem = "A data do pagamento não pode ser no futuro." });
        conta.PagaEm = r.PagaEm;
        conta.ValorPago = r.ValorPago;
        conta.FormaPagamento = Limpo(r.FormaPagamento);
        conta.PagaPorId = UsuarioId;
        await db.SaveChangesAsync();
        return await UmaAsync(id);
    }

    // POST /api/contas/5/desfazer-pagamento → marcou como paga sem querer
    [HttpPost("{id:int}/desfazer-pagamento")]
    public async Task<ActionResult<ContaPagarResponse>> DesfazerPagamento(int id)
    {
        var conta = await db.ContasPagar.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        conta.PagaEm = null;
        conta.ValorPago = null;
        conta.FormaPagamento = null;
        conta.PagaPorId = null;
        await db.SaveChangesAsync();
        return await UmaAsync(id);
    }

    // ------------------------------------------------------------------ apoio

    private async Task<ContaPagarResponse> UmaAsync(int id) =>
        Situar(await Projetar(db.ContasPagar.AsNoTracking().Where(c => c.Id == id)).FirstAsync(), Relogio.Hoje);

    private static IQueryable<ContaPagarResponse> Projetar(IQueryable<ContaPagar> q) =>
        q.Select(c => new ContaPagarResponse(c.Id, c.Descricao, c.Documento, c.Vencimento, c.Valor, "", 0,
            c.FornecedorId, c.Fornecedor!.Nome, c.NotaEntradaId, c.NotaEntrada!.Numero,
            c.PagaEm, c.ValorPago, c.FormaPagamento, c.PagaPor!.NomeCompleto, c.Observacao));

    private static ContaPagarResponse Situar(ContaPagarResponse c, DateOnly hoje)
    {
        var dias = c.Vencimento.DayNumber - hoje.DayNumber;
        var situacao = c.PagaEm is not null ? "Paga" : dias < 0 ? "Vencida" : dias == 0 ? "VenceHoje" : "Aberta";
        return c with { Situacao = situacao, DiasParaVencer = dias };
    }

    private async Task<bool> FornecedorInvalidoAsync(int? id) => id is int f && !await db.Fornecedores.AnyAsync(x => x.Id == f);

    private static string? Limpo(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
