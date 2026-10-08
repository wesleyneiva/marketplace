using Marketplace.Api.Contracts;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

// Controller "magro": só recebe o pedido e responde. As contas ficam no RelatoriosService
// e a montagem do pedido para a IA no InsightsService (reaproveitados pelo relatório semanal).
[ApiController]
[Route("api/relatorios")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class RelatoriosController(RelatoriosService relatorios, InsightsService insights) : ControllerBase
{
    // GET /api/relatorios/vendas?de=2026-09-09&ate=2026-10-08
    [HttpGet("vendas")]
    public Task<RelatorioVendas> Vendas(DateOnly? de, DateOnly? ate) => relatorios.Vendas(de, ate);

    // GET /api/relatorios/produtos → curva ABC + categorias
    [HttpGet("produtos")]
    public Task<RelatorioProdutos> Produtos(DateOnly? de, DateOnly? ate) => relatorios.Produtos(de, ate);

    // GET /api/relatorios/clima → o clima influenciou as vendas?
    [HttpGet("clima")]
    public Task<RelatorioClima> Clima(DateOnly? de, DateOnly? ate) => relatorios.Clima(de, ate);

    // POST /api/relatorios/insights?de=…&ate=… → a IA (Gemini, no n8n) comenta o período e sugere ações.
    [HttpPost("insights")]
    public async Task<IActionResult> Insights(DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var periodo = RelatoriosService.Ler(de, ate);
        var (dados, prompt) = await insights.MontarAsync(periodo, "", ct);
        var r = await insights.PedirAoN8nAsync(dados, prompt, ct);
        return r.Ok
            ? Ok(new { insights = r.Texto, periodo, geradoEm = DateTimeOffset.UtcNow })
            : StatusCode(r.Status, new { mensagem = r.Mensagem });
    }
}
