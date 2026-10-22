using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

// Troca e vale-troca (ver Services/TrocaService.cs). A troca é feita no caixa; o relatório fica em Vendas → Trocas e vales.
[ApiController]
[Route("api/trocas")]
[Authorize]
public class TrocasController(TrocaService trocas, AutorizacaoService autorizacoes) : ControllerBase
{
    private const string Gerencia = $"{Perfis.Administrador},{Perfis.Gerente}";
    private string UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool EhGerencia => User.IsInRole(Perfis.Administrador) || User.IsInRole(Perfis.Gerente);

    // GET /api/trocas/venda/123 → a venda e o que ainda dá para devolver
    [HttpGet("venda/{id:int}")]
    public Task<ActionResult<VendaParaTrocaResponse>> Venda(int id) => Executar(() => trocas.ConsultarVendaAsync(id));

    // GET /api/trocas/vendas-com-produto/45 → vendas recentes com o produto (cliente sem o cupom)
    [HttpGet("vendas-com-produto/{produtoId:int}")]
    public Task<List<VendaComProdutoResponse>> VendasComProduto(int produtoId) => trocas.VendasComProdutoAsync(produtoId);

    // POST /api/trocas → registra a troca (itens voltam ao estoque) e gera o vale-troca
    [HttpPost]
    public async Task<ActionResult<TrocaResponse>> Registrar(NovaTrocaRequest r)
    {
        try
        {
            // Fora do prazo: confere a senha do gerente antes (o gerente logado autoriza a si mesmo).
            string? autorizou = null;
            if (await trocas.ForaDoPrazoAsync(r.VendaId)) autorizou = await autorizacoes.ConferirAsync(UsuarioId, EhGerencia, r.Autorizacao);
            var troca = await trocas.RegistrarAsync(r, UsuarioId, autorizou);
            return await trocas.ObterAsync(troca.Id);
        }
        catch (AutorizacaoException e) { return BadRequest(new { mensagem = e.Message, autorizacao = true }); }
        catch (ConflitoEstoqueException e) { return Conflict(new { mensagem = e.Message }); }
        catch (EstoqueException e) { return BadRequest(new { mensagem = e.Message }); }
    }

    [HttpGet("{id:int}")]
    public Task<ActionResult<TrocaResponse>> Obter(int id) => Executar(() => trocas.ObterAsync(id));

    // GET /api/trocas/vale/VT-7K3M9Q → saldo e validade (antes de usar no pagamento)
    [HttpGet("vale/{codigo}")]
    public Task<ActionResult<ValeTrocaResponse>> Vale(string codigo) => Executar(() => trocas.ValePorCodigoAsync(codigo));

    // GET /api/trocas?de=2026-10-01&ate=2026-10-31 → relatório (motivos, produtos, vales em aberto)
    [HttpGet]
    [Authorize(Roles = Gerencia)]
    public Task<TrocasRelatorioResponse> Relatorio(DateOnly? de, DateOnly? ate)
    {
        var fim = ate ?? Relogio.Hoje;
        return trocas.RelatorioAsync(de ?? fim.AddDays(-29), fim);
    }

    private static async Task<ActionResult<T>> Executar<T>(Func<Task<T>> acao)
    {
        try { return await acao(); }
        catch (EstoqueException e) { return new BadRequestObjectResult(new { mensagem = e.Message }); }
    }
}
