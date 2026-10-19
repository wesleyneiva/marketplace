using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// DRE gerencial (competência): o resultado de cada mês, e do que cada linha é feita.
[ApiController]
[Route("api/financeiro/dre")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class DreController(DreService dre, AppDbContext db, CacheCurto cache) : ControllerBase
{
    // GET /api/financeiro/dre?ate=2026-10&meses=6
    [HttpGet]
    public async Task<ActionResult<DreResponse>> Calcular(string? ate, int meses = 6)
    {
        if (!Mes(ate, out var mes)) return BadRequest(new { mensagem = "Mês inválido (use AAAA-MM)." });
        // Guardada 2 min; qualquer gravação no financeiro troca a versão e a próxima abertura recalcula.
        var chave = $"dre:{db.EmpresaAtual}:{CacheCurto.VersaoFinanceiro(db.EmpresaAtual)}:{mes}:{meses}";
        return await cache.ObterAsync(chave, () => dre.CalcularAsync(mes, meses));
    }

    // GET /api/financeiro/dre/detalhe?mes=2026-09&linha=cmv | categoria:12 | grupo:Pessoal ...
    [HttpGet("detalhe")]
    public async Task<ActionResult<DetalheDreResponse>> Detalhe(string mes, string linha)
    {
        if (!Mes(mes, out var m)) return BadRequest(new { mensagem = "Mês inválido (use AAAA-MM)." });
        try { return await dre.DetalharAsync(m, linha); }
        catch (ArgumentException e) { return BadRequest(new { mensagem = e.Message }); }
    }

    // PUT /api/financeiro/dre/aliquota  { "valor": 7.3 }  → alíquota efetiva do Simples (o contador informa)
    [HttpPut("aliquota")]
    public async Task<IActionResult> Aliquota(AliquotaSimplesRequest r)
    {
        await db.Empresas.Where(e => e.Id == db.EmpresaAtual)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AliquotaSimples, Math.Round(r.Valor, 4)));
        return NoContent();
    }

    private static bool Mes(string? texto, out DateOnly mes)
    {
        mes = new DateOnly(Relogio.Hoje.Year, Relogio.Hoje.Month, 1);
        if (string.IsNullOrWhiteSpace(texto)) return true;
        return DateOnly.TryParseExact(texto + "-01", "yyyy-MM-dd", out mes);
    }
}
