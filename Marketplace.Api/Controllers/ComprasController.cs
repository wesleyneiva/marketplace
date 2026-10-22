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
[Route("api/compras")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class ComprasController(AppDbContext db, ComprasService compras) : ControllerBase
{
    private string UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET /api/compras/sugestao → o que comprar, agrupado por fornecedor
    [HttpGet("sugestao")]
    public Task<List<SugestaoFornecedor>> Sugestao() => compras.SugestaoAsync(HttpContext.RequestAborted);

    // GET /api/compras/sugestao/resumo → aviso do painel e número no menu
    [HttpGet("sugestao/resumo")]
    public async Task<ResumoSugestaoResponse> ResumoSugestao()
    {
        var s = await compras.SugestaoAsync(HttpContext.RequestAborted);
        return new(s.Sum(f => f.Itens.Count), s.Count, s.Sum(f => f.Total));
    }

    // GET /api/compras/pedidos?status=Enviado (rascunho nunca: é só o pedido no meio do envio)
    [HttpGet("pedidos")]
    public async Task<List<PedidoResponse>> Pedidos(StatusPedido? status, int quantidade = 50)
    {
        var consulta = db.PedidosCompra.AsNoTracking().Where(p => p.Status != StatusPedido.Rascunho);
        if (status is not null) consulta = consulta.Where(p => p.Status == status);
        var ids = await consulta.OrderByDescending(p => p.Id).Take(Math.Clamp(quantidade, 1, 200)).Select(p => p.Id).ToListAsync();
        return await MontarAsync(ids);
    }

    [HttpGet("pedidos/{id:int}")]
    public async Task<ActionResult<PedidoResponse>> Pedido(int id) =>
        (await MontarAsync([id])).FirstOrDefault() is { } p ? p : NotFound();

    [HttpPost("pedidos")]
    public Task<IActionResult> Criar(NovoPedidoRequest r) =>
        Executar(async () => (await compras.CriarAsync(r, UsuarioId)).Id);

    [HttpPost("pedidos/{id:int}/enviar")]
    public Task<IActionResult> Enviar(int id) => Executar(async () => { await compras.EnviarAsync(id); return id; });

    // DELETE /api/compras/pedidos/5 → desistiu do envio (só rascunho)
    [HttpDelete("pedidos/{id:int}")]
    public async Task<IActionResult> Descartar(int id)
    {
        try { await compras.DescartarRascunhoAsync(id); return NoContent(); }
        catch (EstoqueException e) { return BadRequest(new { mensagem = e.Message }); }
    }

    // PUT /api/compras/pedidos/5/previsao { "previsao": "2026-10-30" } → o fornecedor confirmou outra data de entrega
    [HttpPut("pedidos/{id:int}/previsao")]
    public Task<IActionResult> Previsao(int id, PrevisaoEntregaRequest r) =>
        Executar(async () => { await compras.MudarPrevisaoAsync(id, r.Previsao); return id; });

    [HttpPost("pedidos/{id:int}/cancelar")]
    public Task<IActionResult> Cancelar(int id) => Executar(async () => { await compras.CancelarAsync(id); return id; });

    [HttpPost("pedidos/{id:int}/receber")]
    public Task<IActionResult> Receber(int id, ReceberPedidoRequest r) =>
        Executar(async () => { await compras.ReceberAsync(id, r.Itens, UsuarioId, r.Pagamento); return id; });

    private async Task<IActionResult> Executar(Func<Task<int>> acao)
    {
        try
        {
            var id = await acao();
            return Ok((await MontarAsync([id])).First());
        }
        catch (ConflitoEstoqueException e) { return Conflict(new { mensagem = e.Message }); }
        catch (EstoqueException e) { return BadRequest(new { mensagem = e.Message }); }
    }

    private async Task<List<PedidoResponse>> MontarAsync(List<int> ids)
    {
        var hoje = Services.Relogio.Hoje;
        var pedidos = await db.PedidosCompra.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.FornecedorId, Fornecedor = p.Fornecedor!.Nome, p.Status, p.Automatico, p.Observacao,
                p.CriadoEm, CriadoPor = p.CriadoPor!.NomeCompleto, p.EnviadoEm, p.PrevisaoEntrega, p.RecebidoEm,
                RecebidoPor = p.RecebidoPor != null ? p.RecebidoPor.NomeCompleto : null,
                p.Pagamento,
                Contas = db.ContasPagar.Count(c => c.PedidoCompraId == p.Id || (c.NotaEntrada != null && c.NotaEntrada.PedidoCompraId == p.Id)),
                TemNota = db.NotasEntrada.Any(n => n.PedidoCompraId == p.Id),
                Itens = p.Itens.OrderBy(i => i.Produto!.Nome).Select(i => new ItemPedidoResponse(
                    i.Id, i.ProdutoId, i.Produto!.Nome, i.Produto.Unidade, i.Produto.ControlaValidade, i.Quantidade,
                    i.CustoUnitario, i.QuantidadeRecebida, i.Validade,
                    Math.Round((i.QuantidadeRecebida ?? i.Quantidade) * i.CustoUnitario, 2))).ToList(),
            })
            .ToListAsync();

        return pedidos.OrderByDescending(p => p.Id).Select(p => new PedidoResponse(
            p.Id, p.FornecedorId, p.Fornecedor, p.Status.ToString(), p.Automatico, p.Observacao, p.CriadoEm, p.CriadoPor,
            p.EnviadoEm, p.PrevisaoEntrega, p.RecebidoEm, p.RecebidoPor, p.Itens.Sum(i => i.Total),
            p.Status == StatusPedido.Enviado && p.PrevisaoEntrega < hoje, p.Itens,
            p.Pagamento, p.Contas, p.Status == StatusPedido.Recebido && p.Pagamento == PagamentosPedido.Nota && !p.TemNota)).ToList();
    }
}
