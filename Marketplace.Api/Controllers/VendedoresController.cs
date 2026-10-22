using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Vendedores (quem atendeu) e comissão. A lista dos ativos é aberta ao caixa (escolher na venda).
[ApiController]
[Route("api/vendedores")]
[Authorize]
public class VendedoresController(AppDbContext db, ComissaoService comissao) : ControllerBase
{
    private const string Gerencia = $"{Perfis.Administrador},{Perfis.Gerente}";
    private string UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET /api/vendedores?todos=false → no caixa, só os ativos
    [HttpGet]
    public async Task<List<VendedorResponse>> Listar(bool todos = false) =>
        await db.Vendedores.AsNoTracking().Where(v => todos || v.Ativo).OrderBy(v => v.Nome)
            .Select(v => new VendedorResponse(v.Id, v.Nome, v.ComissaoPercentual, v.Ativo)).ToListAsync();

    [HttpPost]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<VendedorResponse>> Criar(VendedorRequest r)
    {
        var nome = r.Nome.Trim();
        if (await db.Vendedores.AnyAsync(v => v.Nome == nome)) return BadRequest(new { mensagem = "Já existe um vendedor com este nome." });
        var v = new Vendedor { Nome = nome, ComissaoPercentual = Math.Round(r.ComissaoPercentual, 2), Ativo = r.Ativo };
        db.Vendedores.Add(v);
        await db.SaveChangesAsync();
        return new VendedorResponse(v.Id, v.Nome, v.ComissaoPercentual, v.Ativo);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<VendedorResponse>> Editar(int id, VendedorRequest r)
    {
        var v = await db.Vendedores.FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return NotFound();
        var nome = r.Nome.Trim();
        if (await db.Vendedores.AnyAsync(x => x.Nome == nome && x.Id != id)) return BadRequest(new { mensagem = "Já existe um vendedor com este nome." });
        v.Nome = nome;
        v.ComissaoPercentual = Math.Round(r.ComissaoPercentual, 2);
        v.Ativo = r.Ativo;
        await db.SaveChangesAsync();
        return new VendedorResponse(v.Id, v.Nome, v.ComissaoPercentual, v.Ativo);
    }

    // GET /api/vendedores/comissao?mes=2026-10
    [HttpGet("comissao")]
    [Authorize(Roles = Gerencia)]
    public Task<ComissaoMesResponse> Comissao(string? mes) => comissao.CalcularAsync(Mes(mes));

    // POST /api/vendedores/comissao/lancar?mes=2026-09 → contas a pagar do mês fechado
    [HttpPost("comissao/lancar")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<ActionResult<ComissaoMesResponse>> Lancar(string? mes)
    {
        try
        {
            await comissao.LancarAsync(Mes(mes), UsuarioId);
            return await comissao.CalcularAsync(Mes(mes));
        }
        catch (EstoqueException e) { return BadRequest(new { mensagem = e.Message }); }
    }

    // Comissão própria por categoria de produto (vale no lugar do % do vendedor)
    [HttpGet("categorias")]
    [Authorize(Roles = Gerencia)]
    public async Task<List<CategoriaComissao>> Categorias() =>
        await db.Categorias.AsNoTracking().Where(c => c.Ativa).OrderBy(c => c.Nome)
            .Select(c => new CategoriaComissao(c.Id, c.Nome, c.ComissaoPercentual)).ToListAsync();

    [HttpPut("categorias")]
    [Authorize(Roles = Gerencia)]
    public async Task<IActionResult> CategoriaComissao(CategoriaComissaoRequest r)
    {
        var c = await db.Categorias.FirstOrDefaultAsync(x => x.Id == r.CategoriaId);
        if (c is null) return NotFound();
        c.ComissaoPercentual = r.ComissaoPercentual is { } p ? Math.Round(p, 2) : null;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static DateOnly Mes(string? mes) =>
        DateOnly.TryParseExact((mes ?? "") + "-01", "yyyy-MM-dd", out var d) ? d : new DateOnly(Relogio.Hoje.Year, Relogio.Hoje.Month, 1);
}
