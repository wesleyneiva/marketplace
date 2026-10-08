using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Todos os perfis logados podem CONSULTAR produtos (o caixa precisa achar o produto na venda).
// Criar, editar e desativar: só Administrador e Gerente.
[ApiController]
[Route("api/produtos")]
[Authorize]
public class ProdutosController(AppDbContext db) : ControllerBase
{
    private const string PodeEditar = $"{Perfis.Administrador},{Perfis.Gerente}";

    // GET /api/produtos?busca=arroz&categoriaId=1&estoqueBaixo=true&incluirInativos=false&pagina=1&tamanho=20
    [HttpGet]
    public async Task<Pagina<ProdutoResponse>> Listar(
        string? busca, int? categoriaId, bool estoqueBaixo = false, bool incluirInativos = false,
        int pagina = 1, int tamanho = 20)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 100);

        // IQueryable = a consulta vai sendo "montada"; só vai ao banco no ToListAsync/CountAsync.
        var consulta = db.Produtos.AsNoTracking();

        if (!incluirInativos)
            consulta = consulta.Where(p => p.Ativo);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            // ILike = "contém", sem diferenciar maiúsculas/minúsculas; Unaccent = ignora acentos.
            // (recursos do PostgreSQL). Ou o código de barras exato, vindo do leitor.
            consulta = consulta.Where(p =>
                EF.Functions.ILike(EF.Functions.Unaccent(p.Nome), EF.Functions.Unaccent($"%{termo}%"))
                || p.CodigoBarras == termo);
        }

        if (categoriaId is not null)
            consulta = consulta.Where(p => p.CategoriaId == categoriaId);

        if (estoqueBaixo)
            consulta = consulta.Where(p => p.EstoqueAtual <= p.EstoqueMinimo);

        var total = await consulta.CountAsync();

        var itens = await consulta
            .OrderBy(p => p.Nome)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(ParaResposta)
            .ToListAsync();

        return new Pagina<ProdutoResponse>(itens, total, pagina, tamanho);
    }

    // GET /api/produtos/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProdutoResponse>> Obter(int id)
    {
        var produto = await db.Produtos.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(ParaResposta)
            .FirstOrDefaultAsync();

        return produto is null ? NotFound() : produto;
    }

    // POST /api/produtos
    [HttpPost]
    [Authorize(Roles = PodeEditar)]
    public async Task<ActionResult<ProdutoResponse>> Criar(ProdutoRequest request)
    {
        if (await ValidarAsync(request, idAtual: null) is { } erro)
            return erro;

        var produto = new Produto { EstoqueAtual = request.EstoqueInicial };
        Preencher(produto, request);

        db.Produtos.Add(produto);
        await db.SaveChangesAsync();

        // 201 Created + endereço do novo produto (padrão REST).
        return CreatedAtAction(nameof(Obter), new { id = produto.Id }, await ObterResposta(produto.Id));
    }

    // PUT /api/produtos/5
    [HttpPut("{id:int}")]
    [Authorize(Roles = PodeEditar)]
    public async Task<ActionResult<ProdutoResponse>> Atualizar(int id, ProdutoRequest request)
    {
        var produto = await db.Produtos.FindAsync(id);
        if (produto is null)
            return NotFound();

        if (await ValidarAsync(request, idAtual: id) is { } erro)
            return erro;

        Preencher(produto, request);
        produto.AtualizadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return await ObterResposta(id);
    }

    // DELETE /api/produtos/5 → não apaga: desativa (as vendas antigas continuam apontando para ele).
    [HttpDelete("{id:int}")]
    [Authorize(Roles = PodeEditar)]
    public async Task<IActionResult> Desativar(int id)
    {
        var produto = await db.Produtos.FindAsync(id);
        if (produto is null)
            return NotFound();

        produto.Ativo = false;
        produto.AtualizadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/produtos/5/reativar
    [HttpPost("{id:int}/reativar")]
    [Authorize(Roles = PodeEditar)]
    public async Task<IActionResult> Reativar(int id)
    {
        var produto = await db.Produtos.FindAsync(id);
        if (produto is null)
            return NotFound();

        produto.Ativo = true;
        produto.AtualizadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Regras que dependem do banco (as simples já foram checadas pelas [anotações] do DTO).
    private async Task<ActionResult?> ValidarAsync(ProdutoRequest request, int? idAtual)
    {
        if (!Unidades.Todas.Contains(request.Unidade))
            ModelState.AddModelError(nameof(request.Unidade), $"Unidade inválida. Use: {string.Join(", ", Unidades.Todas)}.");

        if (!await db.Categorias.AnyAsync(c => c.Id == request.CategoriaId && c.Ativa))
            ModelState.AddModelError(nameof(request.CategoriaId), "Categoria não encontrada.");

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!string.IsNullOrWhiteSpace(request.CodigoBarras) &&
            await db.Produtos.AnyAsync(p => p.CodigoBarras == request.CodigoBarras && p.Id != idAtual))
            return Conflict(new { mensagem = "Já existe um produto com este código de barras." });

        return null;
    }

    private static void Preencher(Produto produto, ProdutoRequest request)
    {
        produto.CodigoBarras = string.IsNullOrWhiteSpace(request.CodigoBarras) ? null : request.CodigoBarras.Trim();
        produto.Nome = request.Nome.Trim();
        produto.CategoriaId = request.CategoriaId;
        produto.Unidade = request.Unidade;
        produto.PrecoCusto = request.PrecoCusto;
        produto.PrecoVenda = request.PrecoVenda;
        produto.EstoqueMinimo = request.EstoqueMinimo;
        produto.ControlaValidade = request.ControlaValidade;
    }

    private async Task<ProdutoResponse> ObterResposta(int id) =>
        await db.Produtos.AsNoTracking().Where(p => p.Id == id).Select(ParaResposta).FirstAsync();

    // "Projeção": o EF transforma isto em SQL e busca só as colunas necessárias.
    // Margem = quanto do preço de venda é lucro bruto: (venda - custo) / venda.
    private static readonly System.Linq.Expressions.Expression<Func<Produto, ProdutoResponse>> ParaResposta =
        p => new ProdutoResponse(
            p.Id,
            p.CodigoBarras,
            p.Nome,
            p.CategoriaId,
            p.Categoria!.Nome,
            p.Unidade,
            p.PrecoCusto,
            p.PrecoVenda,
            p.PrecoVenda == 0 ? 0 : Math.Round((p.PrecoVenda - p.PrecoCusto) / p.PrecoVenda * 100, 1),
            p.EstoqueAtual,
            p.EstoqueMinimo,
            p.EstoqueAtual <= p.EstoqueMinimo,
            p.ControlaValidade,
            p.Ativo);
}
