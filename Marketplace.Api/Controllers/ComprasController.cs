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

    // GET /api/compras/pedidos?status=Enviado
    [HttpGet("pedidos")]
    public async Task<List<PedidoResponse>> Pedidos(StatusPedido? status, int quantidade = 50)
    {
        var consulta = db.PedidosCompra.AsNoTracking();
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

    [HttpPut("pedidos/{id:int}/itens")]
    public Task<IActionResult> EditarRascunho(int id, List<ItemPedidoRequest> itens) =>
        Executar(async () => { await compras.EditarRascunhoAsync(id, itens); return id; });

    // GET /api/compras/sugestao-do-dia → rascunhos montados sozinhos ao abrir a loja (aviso do painel)
    [HttpGet("sugestao-do-dia")]
    public async Task<SugestaoDoDiaResponse> SugestaoDoDia()
    {
        var ligada = await db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.ComprasAutomaticas).FirstAsync();
        var rascunhos = await db.PedidosCompra.AsNoTracking()
            .Where(p => p.SugestaoAutomatica && p.Status == StatusPedido.Rascunho)
            .Select(p => new { p.CriadoEm, Itens = p.Itens.Count, Total = p.Itens.Sum(i => i.Quantidade * i.CustoUnitario) })
            .ToListAsync();
        return new(rascunhos.Count, rascunhos.Sum(r => r.Itens), Math.Round(rascunhos.Sum(r => r.Total), 2),
            rascunhos.Count == 0 ? null : rascunhos.Max(r => r.CriadoEm), ligada);
    }

    // POST /api/compras/sugestao-do-dia/refazer → monta os rascunhos agora (troca os automáticos não enviados)
    [HttpPost("sugestao-do-dia/refazer")]
    public async Task<SugestaoDoDiaResponse> RefazerSugestaoDoDia([FromServices] IServiceProvider servicos)
    {
        await Services.ComprasAutomaticas.GerarAsync(servicos, Services.Relogio.Hoje, HttpContext.RequestAborted);
        return await SugestaoDoDia();
    }

    [HttpPost("pedidos/{id:int}/enviar")]
    public Task<IActionResult> Enviar(int id) => Executar(async () => { await compras.EnviarAsync(id); return id; });

    [HttpPost("pedidos/{id:int}/cancelar")]
    public Task<IActionResult> Cancelar(int id) => Executar(async () => { await compras.CancelarAsync(id); return id; });

    [HttpPost("pedidos/{id:int}/receber")]
    public Task<IActionResult> Receber(int id, ReceberPedidoRequest r) =>
        Executar(async () => { await compras.ReceberAsync(id, r.Itens, UsuarioId); return id; });

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
                p.Id, p.FornecedorId, Fornecedor = p.Fornecedor!.Nome, p.Status, p.Automatico, p.SugestaoAutomatica, p.Observacao,
                p.CriadoEm, CriadoPor = p.CriadoPor!.NomeCompleto, p.EnviadoEm, p.PrevisaoEntrega, p.RecebidoEm,
                RecebidoPor = p.RecebidoPor != null ? p.RecebidoPor.NomeCompleto : null,
                Itens = p.Itens.OrderBy(i => i.Produto!.Nome).Select(i => new ItemPedidoResponse(
                    i.Id, i.ProdutoId, i.Produto!.Nome, i.Produto.Unidade, i.Produto.ControlaValidade, i.Quantidade,
                    i.CustoUnitario, i.QuantidadeRecebida, i.Validade,
                    Math.Round((i.QuantidadeRecebida ?? i.Quantidade) * i.CustoUnitario, 2))).ToList(),
            })
            .ToListAsync();

        return pedidos.OrderByDescending(p => p.Id).Select(p => new PedidoResponse(
            p.Id, p.FornecedorId, p.Fornecedor, p.Status.ToString(), p.Automatico, p.SugestaoAutomatica, p.Observacao, p.CriadoEm, p.CriadoPor,
            p.EnviadoEm, p.PrevisaoEntrega, p.RecebidoEm, p.RecebidoPor, p.Itens.Sum(i => i.Total),
            p.Status == StatusPedido.Enviado && p.PrevisaoEntrega < hoje, p.Itens)).ToList();
    }
}
