using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Fluxo de caixa: realizado (para trás) e previsto (para a frente), e o saldo mínimo de segurança da empresa.
[ApiController]
[Route("api/financeiro/fluxo")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class FluxoCaixaController(FluxoCaixaService fluxo, AppDbContext db) : ControllerBase
{
    // GET /api/financeiro/fluxo/realizado?de=2026-09-01&ate=2026-09-30&agrupar=dia|semana|mes
    [HttpGet("realizado")]
    public async Task<ActionResult<FluxoRealizadoResponse>> Realizado(DateOnly? de, DateOnly? ate, string agrupar = "dia")
    {
        if (agrupar is not ("dia" or "semana" or "mes")) return BadRequest(new { mensagem = "Agrupar por dia, semana ou mes." });
        var fim = ate ?? Relogio.Hoje;
        var inicio = de ?? fim.AddDays(-29);
        if (Math.Abs(fim.DayNumber - inicio.DayNumber) > 731) return BadRequest(new { mensagem = "Período máximo: 2 anos." });
        return await fluxo.RealizadoAsync(inicio, fim, agrupar);
    }

    // GET /api/financeiro/fluxo/previsto?dias=30&estimativa=true
    [HttpGet("previsto")]
    public Task<FluxoPrevistoResponse> Previsto(int dias = 30, bool estimativa = true) => fluxo.PrevistoAsync(dias, estimativa);

    // PUT /api/financeiro/fluxo/saldo-minimo  { "valor": 10000 }
    [HttpPut("saldo-minimo")]
    public async Task<IActionResult> SaldoMinimo(SaldoMinimoRequest r)
    {
        await db.Empresas.Where(e => e.Id == db.EmpresaAtual)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.SaldoMinimoCaixa, Math.Round(r.Valor, 2)));
        return NoContent();
    }
}
