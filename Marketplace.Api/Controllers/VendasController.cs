using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/vendas")]
[Authorize]
public class VendasController(VendaService vendas, CaixaService caixa, AppDbContext db) : ControllerBase
{
    private const string Gerencia = $"{Perfis.Administrador},{Perfis.Gerente}";
    private string UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool EhGerencia => User.IsInRole(Perfis.Administrador) || User.IsInRole(Perfis.Gerente);

    // POST /api/vendas → finaliza uma venda no MEU caixa aberto.
    [HttpPost]
    public async Task<ActionResult<VendaResponse>> Finalizar(NovaVendaRequest pedido)
    {
        try
        {
            var venda = await vendas.FinalizarAsync(UsuarioId, podeDescontoLivre: EhGerencia, pedido);
            return CreatedAtAction(nameof(Obter), new { id = venda.Id }, await MontarCupomAsync(venda.Id));
        }
        catch (ConflitoEstoqueException e)
        {
            return Conflict(new { mensagem = e.Message });
        }
        catch (EstoqueException e)
        {
            return BadRequest(new { mensagem = e.Message });
        }
    }

    // GET /api/vendas/5 → o "cupom". Caixa só vê as vendas que ele mesmo fez.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendaResponse>> Obter(int id)
    {
        var dono = await db.Vendas.Where(v => v.Id == id).Select(v => v.UsuarioId).FirstOrDefaultAsync();
        if (dono is null) return NotFound();
        if (!EhGerencia && dono != UsuarioId) return Forbid();
        return await MontarCupomAsync(id);
    }

    // GET /api/vendas?data=2026-10-08&sessaoId=3&pagina=1
    // Gerência vê tudo; Caixa vê só as vendas do caixa aberto dele.
    [HttpGet]
    public async Task<Pagina<VendaListaResponse>> Listar(DateOnly? data, int? sessaoId, int pagina = 1, int tamanho = 20)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 100);
        var consulta = db.Vendas.AsNoTracking();

        if (!EhGerencia)
        {
            var minha = await caixa.ObterSessaoAbertaAsync(UsuarioId);
            consulta = consulta.Where(v => v.SessaoCaixaId == (minha != null ? minha.Id : -1));
        }
        if (sessaoId is not null) consulta = consulta.Where(v => v.SessaoCaixaId == sessaoId);
        if (data is not null)
        {
            var (inicio, fim) = DiaEmUtc(data.Value);
            consulta = consulta.Where(v => v.DataHora >= inicio && v.DataHora < fim);
        }

        var total = await consulta.CountAsync();
        var itens = await consulta.OrderByDescending(v => v.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(v => new VendaListaResponse(
                v.Id, v.DataHora, v.SessaoCaixa!.NumeroCaixa, v.Usuario!.NomeCompleto, v.Status.ToString(),
                v.Itens.Count, v.Total,
                string.Join(" + ", v.Pagamentos.Select(p => p.Forma.ToString()))))
            .ToListAsync();

        return new Pagina<VendaListaResponse>(itens, total, pagina, tamanho);
    }

    // POST /api/vendas/5/cancelar → só gerência (é o famoso "chama o gerente!").
    [HttpPost("{id:int}/cancelar")]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<VendaResponse>> Cancelar(int id, CancelarVendaRequest r)
    {
        try
        {
            await vendas.CancelarAsync(id, UsuarioId, r.Motivo);
            return await MontarCupomAsync(id);
        }
        catch (ConflitoEstoqueException e)
        {
            return Conflict(new { mensagem = e.Message });
        }
        catch (EstoqueException e)
        {
            return BadRequest(new { mensagem = e.Message });
        }
    }

    // GET /api/vendas/resumo?data=2026-10-08 → números do dia para o dashboard.
    [HttpGet("resumo")]
    [Authorize(Roles = Gerencia)]
    public async Task<ResumoVendasResponse> Resumo(DateOnly? data)
    {
        var dia = data ?? Relogio.HojeBrasilia;
        var (inicio, fim) = DiaEmUtc(dia);
        var doDia = db.Vendas.AsNoTracking()
            .Where(v => v.Status == StatusVenda.Concluida && v.DataHora >= inicio && v.DataHora < fim);

        var quantidade = await doDia.CountAsync();
        var total = await doDia.SumAsync(v => (decimal?)v.Total) ?? 0;
        var troco = await doDia.SumAsync(v => (decimal?)v.Troco) ?? 0;
        var desconto = await doDia.SumAsync(v => (decimal?)v.Desconto) ?? 0;
        var custo = await doDia.SelectMany(v => v.Itens).SumAsync(i => (decimal?)(i.Quantidade * i.CustoUnitario)) ?? 0;

        var porForma = await doDia.SelectMany(v => v.Pagamentos)
            .GroupBy(p => p.Forma).Select(g => new { g.Key, Valor = g.Sum(p => p.Valor) }).ToListAsync();
        var formas = Enum.GetValues<FormaPagamento>()
            .Select(f => new TotalPorForma(f.ToString(),
                (porForma.FirstOrDefault(x => x.Key == f)?.Valor ?? 0) - (f == FormaPagamento.Dinheiro ? troco : 0)))
            .ToList();

        // Agrupa por hora de Brasília (os horários estão guardados em UTC, 3 h à frente).
        var horarios = await doDia.Select(v => new { v.DataHora, v.Total }).ToListAsync();
        var porHora = horarios
            .GroupBy(v => v.DataHora.ToOffset(TimeSpan.FromHours(-3)).Hour)
            .Select(g => new VendasPorHora(g.Key, g.Count(), g.Sum(v => v.Total)))
            .OrderBy(h => h.Hora)
            .ToList();

        return new ResumoVendasResponse(dia, quantidade, total,
            quantidade == 0 ? 0 : Math.Round(total / quantidade, 2),
            Math.Round(total - custo, 2), formas, porHora);
    }

    private async Task<VendaResponse> MontarCupomAsync(int id) =>
        await db.Vendas.AsNoTracking().Where(v => v.Id == id)
            .Select(v => new VendaResponse(
                v.Id, v.DataHora, v.SessaoCaixa!.NumeroCaixa, v.Usuario!.NomeCompleto, v.Status.ToString(),
                v.Subtotal, v.Desconto, v.Total, v.ValorPago, v.Troco,
                v.Itens.OrderBy(i => i.Id).Select(i => new ItemVendaResponse(i.ProdutoId, i.Descricao, i.Unidade, i.Quantidade, i.PrecoUnitario, i.Total)).ToList(),
                v.Pagamentos.OrderBy(p => p.Id).Select(p => new PagamentoResponse(p.Forma.ToString(), p.Valor)).ToList(),
                v.MotivoCancelamento))
            .FirstAsync();

    private static (DateTimeOffset Inicio, DateTimeOffset Fim) DiaEmUtc(DateOnly dia) =>
        (Relogio.InicioDoDiaUtc(dia), Relogio.InicioDoDiaUtc(dia.AddDays(1)));
}
