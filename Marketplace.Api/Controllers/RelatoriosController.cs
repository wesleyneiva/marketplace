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
public class RelatoriosController(RelatoriosService relatorios, InsightsService insights, InsightsTarefas tarefas,
    Marketplace.Api.Data.AppDbContext db, CacheCurto cache) : ControllerBase
{
    // Os três relatórios ficam guardados 2 min por empresa e período (CacheCurto).
    private string Chave(string tipo, DateOnly? de, DateOnly? ate) => $"rel:{db.EmpresaAtual}:{tipo}:{de}:{ate}";

    // GET /api/relatorios/vendas?de=2026-09-09&ate=2026-10-08
    [HttpGet("vendas")]
    public Task<RelatorioVendas> Vendas(DateOnly? de, DateOnly? ate) => cache.ObterAsync(Chave("vendas", de, ate), () => relatorios.Vendas(de, ate));

    // GET /api/relatorios/produtos → curva ABC + categorias
    [HttpGet("produtos")]
    public Task<RelatorioProdutos> Produtos(DateOnly? de, DateOnly? ate) => cache.ObterAsync(Chave("produtos", de, ate), () => relatorios.Produtos(de, ate));

    // GET /api/relatorios/clima → o clima influenciou as vendas?
    [HttpGet("clima")]
    public Task<RelatorioClima> Clima(DateOnly? de, DateOnly? ate) => cache.ObterAsync(Chave("clima", de, ate), () => relatorios.Clima(de, ate));

    // POST /api/relatorios/insights?de=…&ate=… → começa a análise da IA (Gemini, no n8n) em segundo plano.
    // Devolve { id }: a tela pergunta em GET /api/relatorios/insights/{id} até ficar pronto.
    [HttpPost("insights")]
    public IActionResult Insights(DateOnly? de, DateOnly? ate)
    {
        var tarefa = tarefas.Iniciar(db.EmpresaAtual, RelatoriosService.Ler(de, ate));
        return Accepted(new { id = tarefa.Id });
    }

    // GET /api/relatorios/insights/{id} → { situacao: andamento | pronto | erro, insights?, mensagem? }
    [HttpGet("insights/{id}")]
    public IActionResult InsightsSituacao(string id)
    {
        var t = tarefas.Obter(id, db.EmpresaAtual);
        if (t is null) return NotFound(new { mensagem = "Essa análise não existe mais. Peça de novo." });
        return Ok(new { situacao = t.Situacao, insights = t.Texto, mensagem = t.Erro, segundos = (int)(DateTimeOffset.UtcNow - t.Iniciada).TotalSeconds });
    }
}
