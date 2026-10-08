using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/categorias")]
[Authorize]
public class CategoriasController(AppDbContext db) : ControllerBase
{
    private const string PodeEditar = $"{Perfis.Administrador},{Perfis.Gerente}";

    // GET /api/categorias → todas, com quantos produtos ativos cada uma tem.
    [HttpGet]
    public async Task<List<CategoriaResponse>> Listar() =>
        await db.Categorias.AsNoTracking()
            .OrderBy(c => c.Nome)
            .Select(c => new CategoriaResponse(c.Id, c.Nome, c.Ativa, c.Produtos.Count(p => p.Ativo)))
            .ToListAsync();

    // POST /api/categorias
    [HttpPost]
    [Authorize(Roles = PodeEditar)]
    public async Task<ActionResult<CategoriaResponse>> Criar(CategoriaRequest request)
    {
        var nome = request.Nome.Trim();
        if (await db.Categorias.AnyAsync(c => c.Nome.ToLower() == nome.ToLower()))
            return Conflict(new { mensagem = "Já existe uma categoria com este nome." });

        var categoria = new Categoria { Nome = nome };
        db.Categorias.Add(categoria);
        await db.SaveChangesAsync();

        return Created($"/api/categorias/{categoria.Id}", new CategoriaResponse(categoria.Id, categoria.Nome, true, 0));
    }

    // PUT /api/categorias/3 → renomear
    [HttpPut("{id:int}")]
    [Authorize(Roles = PodeEditar)]
    public async Task<IActionResult> Renomear(int id, CategoriaRequest request)
    {
        var categoria = await db.Categorias.FindAsync(id);
        if (categoria is null)
            return NotFound();

        var nome = request.Nome.Trim();
        if (await db.Categorias.AnyAsync(c => c.Id != id && c.Nome.ToLower() == nome.ToLower()))
            return Conflict(new { mensagem = "Já existe uma categoria com este nome." });

        categoria.Nome = nome;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
