using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Grade de tamanho e cor (Models/Modelo.cs): o modelo agrupa as variações; cada variação é um Produto.
// Consultar: todos os perfis (o caixa escolhe o tamanho). Criar, editar e dar entrada: Administrador e Gerente.
[ApiController]
[Route("api/modelos")]
[Authorize]
public class ModelosController(AppDbContext db, EstoqueService estoque) : ControllerBase
{
    private const string PodeEditar = $"{Perfis.Administrador},{Perfis.Gerente}";
    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // GET /api/modelos?busca=asics&categoriaId=2&incluirInativos=false&pagina=1&tamanho=20
    [HttpGet]
    public async Task<Pagina<ModeloResumoResponse>> Listar(string? busca, int? categoriaId, bool incluirInativos = false,
        int pagina = 1, int tamanho = 20)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 100);
        var consulta = db.Modelos.AsNoTracking();
        if (!incluirInativos) consulta = consulta.Where(m => m.Ativo);
        if (categoriaId is not null) consulta = consulta.Where(m => m.CategoriaId == categoriaId);
        foreach (var palavra in (busca ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6).Select(SemCuringa))
        {
            var termo = $"%{palavra}%";
            consulta = consulta.Where(m => EF.Functions.ILike(EF.Functions.Unaccent(m.Nome), EF.Functions.Unaccent(termo))
                || EF.Functions.ILike(m.Referencia ?? "", termo) || EF.Functions.ILike(EF.Functions.Unaccent(m.Marca ?? ""), EF.Functions.Unaccent(termo)));
        }

        var total = await consulta.CountAsync();
        var itens = await consulta.OrderBy(m => m.Nome).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(m => new ModeloResumoResponse(m.Id, m.Nome, m.Referencia, m.Marca, m.CategoriaId, m.Categoria!.Nome,
                m.Fornecedor != null ? m.Fornecedor.Nome : null,
                m.Variacoes.Where(v => v.Ativo).Min(v => (decimal?)v.PrecoVenda) ?? 0,
                m.Variacoes.Where(v => v.Ativo).Max(v => (decimal?)v.PrecoVenda) ?? 0,
                m.Variacoes.Count(v => v.Ativo),
                m.Variacoes.Where(v => v.Ativo).Sum(v => (decimal?)v.EstoqueAtual) ?? 0,
                m.Variacoes.Count(v => v.Ativo && v.EstoqueAtual <= 0),
                m.Ativo, m.Tamanhos, m.Cores))
            .ToListAsync();
        return new Pagina<ModeloResumoResponse>(itens, total, pagina, tamanho);
    }

    // GET /api/modelos/5 → o modelo com a matriz (cada variação com estoque, preço e código).
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ModeloDetalheResponse>> Obter(int id) =>
        await DetalharAsync(id) is { } m ? m : NotFound();

    // POST /api/modelos → cria o modelo e uma variação (produto) para cada combinação tamanho × cor escolhida.
    [HttpPost]
    [Authorize(Roles = PodeEditar)]
    public async Task<ActionResult<ModeloDetalheResponse>> Criar(NovoModeloRequest r)
    {
        var tamanhos = Limpar(r.Tamanhos, 20);
        var cores = Limpar(r.Cores, 40);
        if (tamanhos.Count == 0 || cores.Count == 0) return BadRequest(new { mensagem = "Informe pelo menos um tamanho e uma cor." });
        if (tamanhos.Count * cores.Count > 300) return BadRequest(new { mensagem = "Grade grande demais (máximo 300 variações)." });
        if (await ValidarAsync(r.CategoriaId, r.FornecedorId, r.Unidade) is { } erro) return BadRequest(new { mensagem = erro });

        var combinacoes = r.Combinacoes is { Count: > 0 }
            ? r.Combinacoes.Select(c => c with { Tamanho = c.Tamanho.Trim(), Cor = c.Cor.Trim() }).ToList()
            : [.. tamanhos.SelectMany(t => cores.Select(c => new CombinacaoRequest(t, c)))];
        if (combinacoes.Any(c => !tamanhos.Contains(c.Tamanho) || !cores.Contains(c.Cor)))
            return BadRequest(new { mensagem = "Combinação com tamanho ou cor que não está na grade." });
        if (combinacoes.GroupBy(c => (c.Tamanho, c.Cor)).Any(g => g.Count() > 1))
            return BadRequest(new { mensagem = "Combinação repetida." });
        if (await CodigosEmUsoAsync(combinacoes) is { } usado) return Conflict(new { mensagem = usado });

        var modelo = new Modelo
        {
            Nome = r.Nome.Trim(), Referencia = Vazio(r.Referencia), Marca = Vazio(r.Marca), CategoriaId = r.CategoriaId,
            FornecedorId = r.FornecedorId, Tamanhos = [.. tamanhos], Cores = [.. cores],
        };
        db.Modelos.Add(modelo);
        var codigos = await CodigoInterno.ReservarAsync(db, combinacoes.Count(c => string.IsNullOrWhiteSpace(c.CodigoBarras)));
        foreach (var c in combinacoes)
            modelo.Variacoes.Add(new Produto
            {
                Nome = Modelo.NomeDaVariacao(modelo.Nome, c.Tamanho, c.Cor), Tamanho = c.Tamanho, Cor = c.Cor,
                CodigoBarras = string.IsNullOrWhiteSpace(c.CodigoBarras) ? codigos.Dequeue() : c.CodigoBarras.Trim(),
                CategoriaId = r.CategoriaId, FornecedorId = r.FornecedorId, Unidade = r.Unidade,
                PrecoCusto = r.PrecoCusto, PrecoVenda = r.PrecoVenda, EstoqueMinimo = r.EstoqueMinimo, Ncm = Vazio(r.Ncm),
            });
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = modelo.Id }, await DetalharAsync(modelo.Id));
    }

    // PUT /api/modelos/5 → muda o modelo e repassa para as variações (nome do cupom, categoria, fornecedor, NCM;
    // preços e mínimo só com "aplicar").
    [HttpPut("{id:int}")]
    [Authorize(Roles = PodeEditar)]
    public async Task<ActionResult<ModeloDetalheResponse>> Editar(int id, EditarModeloRequest r)
    {
        var modelo = await db.Modelos.Include(m => m.Variacoes).FirstOrDefaultAsync(m => m.Id == id);
        if (modelo is null) return NotFound();
        var unidade = modelo.Variacoes.FirstOrDefault()?.Unidade ?? Unidades.Unidade;
        if (await ValidarAsync(r.CategoriaId, r.FornecedorId, unidade) is { } erro) return BadRequest(new { mensagem = erro });

        modelo.Nome = r.Nome.Trim();
        modelo.Referencia = Vazio(r.Referencia);
        modelo.Marca = Vazio(r.Marca);
        modelo.CategoriaId = r.CategoriaId;
        modelo.FornecedorId = r.FornecedorId;
        var agora = DateTimeOffset.UtcNow;
        foreach (var v in modelo.Variacoes)
        {
            v.Nome = Modelo.NomeDaVariacao(modelo.Nome, v.Tamanho, v.Cor);
            v.CategoriaId = r.CategoriaId;
            v.FornecedorId = r.FornecedorId;
            if (r.Ncm is not null) v.Ncm = Vazio(r.Ncm);
            if (r.AplicarPrecos)
            {
                v.PrecoCusto = r.PrecoCusto;
                v.PrecoVenda = r.PrecoVenda;
                v.EstoqueMinimo = r.EstoqueMinimo;
            }
            v.AtualizadoEm = agora;
        }
        await db.SaveChangesAsync();
        return await DetalharAsync(id) is { } m ? m : NotFound();
    }

    // POST /api/modelos/5/variacoes → acrescenta combinações (tamanho/cor novos entram no fim da grade).
    // As que já existem são ignoradas; uma desativada volta a ficar ativa.
    [HttpPost("{id:int}/variacoes")]
    [Authorize(Roles = PodeEditar)]
    public async Task<ActionResult<ModeloDetalheResponse>> AcrescentarVariacoes(int id, NovasVariacoesRequest r)
    {
        var modelo = await db.Modelos.Include(m => m.Variacoes).FirstOrDefaultAsync(m => m.Id == id);
        if (modelo is null) return NotFound();
        var pedidas = r.Combinacoes.Select(c => c with { Tamanho = c.Tamanho.Trim(), Cor = c.Cor.Trim() })
            .Where(c => c.Tamanho.Length > 0 && c.Cor.Length > 0).DistinctBy(c => (c.Tamanho, c.Cor)).ToList();
        var novas = pedidas.Where(c => !modelo.Variacoes.Any(v => v.Tamanho == c.Tamanho && v.Cor == c.Cor)).ToList();
        foreach (var v in modelo.Variacoes.Where(v => !v.Ativo && pedidas.Any(c => c.Tamanho == v.Tamanho && c.Cor == v.Cor)))
            v.Ativo = true;
        if (modelo.Variacoes.Count + novas.Count > 300) return BadRequest(new { mensagem = "Grade grande demais (máximo 300 variações)." });
        if (await CodigosEmUsoAsync(novas) is { } usado) return Conflict(new { mensagem = usado });

        modelo.Tamanhos = [.. modelo.Tamanhos.Concat(novas.Select(c => c.Tamanho)).Distinct()];
        modelo.Cores = [.. modelo.Cores.Concat(novas.Select(c => c.Cor)).Distinct()];
        var base_ = modelo.Variacoes.OrderBy(v => v.Id).FirstOrDefault(); // preço, unidade e fiscal vêm da 1ª variação
        var codigos = await CodigoInterno.ReservarAsync(db, novas.Count(c => string.IsNullOrWhiteSpace(c.CodigoBarras)));
        foreach (var c in novas)
            modelo.Variacoes.Add(new Produto
            {
                Nome = Modelo.NomeDaVariacao(modelo.Nome, c.Tamanho, c.Cor), Tamanho = c.Tamanho, Cor = c.Cor,
                CodigoBarras = string.IsNullOrWhiteSpace(c.CodigoBarras) ? codigos.Dequeue() : c.CodigoBarras.Trim(),
                CategoriaId = modelo.CategoriaId, FornecedorId = modelo.FornecedorId, Unidade = base_?.Unidade ?? Unidades.Unidade,
                PrecoCusto = base_?.PrecoCusto ?? 0, PrecoVenda = base_?.PrecoVenda ?? 0.01m, EstoqueMinimo = base_?.EstoqueMinimo ?? 0,
                Ncm = base_?.Ncm,
            });
        await db.SaveChangesAsync();
        return await DetalharAsync(id) is { } m ? m : NotFound();
    }

    // POST /api/modelos/5/entrada → entrada de mercadoria na grade (uma movimentação por variação, tudo junto).
    [HttpPost("{id:int}/entrada")]
    [Authorize(Roles = PodeEditar)]
    public async Task<ActionResult<ModeloDetalheResponse>> Entrada(int id, EntradaGradeRequest r)
    {
        var ids = await db.Produtos.Where(p => p.ModeloId == id).Select(p => p.Id).ToListAsync();
        if (ids.Count == 0) return NotFound();
        if (r.Itens.Any(i => !ids.Contains(i.ProdutoId))) return BadRequest(new { mensagem = "Variação que não é deste modelo." });
        await using var transacao = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in r.Itens.Where(i => i.Quantidade > 0))
                await estoque.RegistrarEntradaAsync(item.ProdutoId, item.Quantidade, r.CustoUnitario, null,
                    string.IsNullOrWhiteSpace(r.Observacao) ? "Entrada em grade" : r.Observacao.Trim(), UsuarioId);
            await transacao.CommitAsync();
        }
        catch (EstoqueException e)
        {
            return BadRequest(new { mensagem = e.Message });
        }
        return await DetalharAsync(id) is { } m ? m : NotFound();
    }

    // DELETE /api/modelos/5 → desativa o modelo e as variações (as vendas antigas continuam apontando para elas).
    [HttpDelete("{id:int}")]
    [Authorize(Roles = PodeEditar)]
    public Task<IActionResult> Desativar(int id) => MudarAtivoAsync(id, false);

    [HttpPost("{id:int}/reativar")]
    [Authorize(Roles = PodeEditar)]
    public Task<IActionResult> Reativar(int id) => MudarAtivoAsync(id, true);

    private async Task<IActionResult> MudarAtivoAsync(int id, bool ativo)
    {
        var modelo = await db.Modelos.Include(m => m.Variacoes).FirstOrDefaultAsync(m => m.Id == id);
        if (modelo is null) return NotFound();
        modelo.Ativo = ativo;
        foreach (var v in modelo.Variacoes) { v.Ativo = ativo; v.AtualizadoEm = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ModeloDetalheResponse?> DetalharAsync(int id)
    {
        var m = await db.Modelos.AsNoTracking().Include(x => x.Categoria).Include(x => x.Fornecedor)
            .Include(x => x.Variacoes).FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return null;
        // Na ordem da grade: tamanho (como cadastrado) e depois cor.
        int Pos(string[] lista, string? valor) => Array.IndexOf(lista, valor) is var i && i >= 0 ? i : int.MaxValue;
        var variacoes = m.Variacoes.OrderBy(v => Pos(m.Tamanhos, v.Tamanho)).ThenBy(v => Pos(m.Cores, v.Cor))
            .Select(v => new VariacaoResponse(v.Id, v.Nome, v.Tamanho ?? "", v.Cor ?? "", v.CodigoBarras, v.PrecoCusto, v.PrecoVenda,
                v.EstoqueAtual, v.EstoqueMinimo, v.Ativo)).ToList();
        var primeira = m.Variacoes.OrderBy(v => v.Id).FirstOrDefault();
        return new ModeloDetalheResponse(m.Id, m.Nome, m.Referencia, m.Marca, m.CategoriaId, m.Categoria!.Nome, m.FornecedorId,
            m.Fornecedor?.Nome, primeira?.Unidade ?? Unidades.Unidade, primeira?.Ncm, m.Tamanhos, m.Cores, m.Ativo, variacoes);
    }

    private async Task<string?> ValidarAsync(int categoriaId, int? fornecedorId, string unidade)
    {
        if (!Unidades.Todas.Contains(unidade)) return $"Unidade inválida. Use: {string.Join(", ", Unidades.Todas)}.";
        if (!await db.Categorias.AnyAsync(c => c.Id == categoriaId && c.Ativa)) return "Categoria não encontrada.";
        if (fornecedorId is not null && !await db.Fornecedores.AnyAsync(f => f.Id == fornecedorId)) return "Fornecedor não encontrado.";
        return null;
    }

    private async Task<string?> CodigosEmUsoAsync(List<CombinacaoRequest> combinacoes)
    {
        var codigos = combinacoes.Where(c => !string.IsNullOrWhiteSpace(c.CodigoBarras)).Select(c => c.CodigoBarras!.Trim()).ToList();
        if (codigos.Count != codigos.Distinct().Count()) return "Código de barras repetido na grade.";
        var usado = await db.Produtos.Where(p => p.CodigoBarras != null && codigos.Contains(p.CodigoBarras))
            .Select(p => new { p.CodigoBarras, p.Nome }).FirstOrDefaultAsync();
        return usado is null ? null : $"O código {usado.CodigoBarras} já é do produto \"{usado.Nome}\".";
    }

    // Sem vazios nem repetidos, na ordem em que vieram.
    private static List<string> Limpar(IEnumerable<string>? lista, int maximo) =>
        [.. (lista ?? []).Select(t => t.Trim()).Where(t => t.Length > 0 && t.Length <= maximo).Distinct()];

    private static string? Vazio(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
    private static string SemCuringa(string texto) => texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
