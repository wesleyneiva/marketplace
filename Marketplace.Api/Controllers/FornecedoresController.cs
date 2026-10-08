using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/fornecedores")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class FornecedoresController(AppDbContext db) : ControllerBase
{
    // GET /api/fornecedores?incluirInativos=false
    [HttpGet]
    public async Task<List<FornecedorResponse>> Listar(bool incluirInativos = false) =>
        await db.Fornecedores.AsNoTracking()
            .Where(f => incluirInativos || f.Ativo)
            .OrderBy(f => f.Nome)
            .Select(f => new FornecedorResponse(f.Id, f.Nome, f.Cnpj, f.Contato, f.Telefone, f.Email, f.PrazoEntregaDias,
                f.Observacao, f.Ativo, f.Produtos.Count(p => p.Ativo),
                db.PedidosCompra.Count(p => p.FornecedorId == f.Id && (p.Status == StatusPedido.Rascunho || p.Status == StatusPedido.Enviado))))
            .ToListAsync();

    [HttpPost]
    public async Task<IActionResult> Criar(FornecedorRequest r)
    {
        if (await NomeEmUsoAsync(r.Nome, null)) return Conflict(new { mensagem = "Já existe um fornecedor com este nome." });
        var f = new Fornecedor();
        Preencher(f, r);
        db.Fornecedores.Add(f);
        await db.SaveChangesAsync();
        return Ok(new { f.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, FornecedorRequest r)
    {
        var f = await db.Fornecedores.FindAsync(id);
        if (f is null) return NotFound();
        if (await NomeEmUsoAsync(r.Nome, id)) return Conflict(new { mensagem = "Já existe um fornecedor com este nome." });
        Preencher(f, r);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/fornecedores/5/ativo?valor=false → desativar/reativar
    [HttpPost("{id:int}/ativo")]
    public async Task<IActionResult> Ativo(int id, bool valor)
    {
        var f = await db.Fornecedores.FindAsync(id);
        if (f is null) return NotFound();
        f.Ativo = valor;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<bool> NomeEmUsoAsync(string nome, int? id) =>
        db.Fornecedores.AnyAsync(f => f.Nome.ToLower() == nome.Trim().ToLower() && f.Id != id);

    private static void Preencher(Fornecedor f, FornecedorRequest r)
    {
        static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        f.Nome = r.Nome.Trim();
        f.Cnpj = Limpo(r.Cnpj);
        f.Contato = Limpo(r.Contato);
        f.Telefone = Limpo(r.Telefone);
        f.Email = Limpo(r.Email);
        f.PrazoEntregaDias = r.PrazoEntregaDias;
        f.Observacao = Limpo(r.Observacao);
    }
}
