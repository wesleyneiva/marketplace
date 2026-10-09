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

        var termo = busca?.Trim() ?? "";
        if (termo.Length > 0)
        {
            // Cada PALAVRA precisa aparecer no nome, em qualquer ordem: "refri guar" acha "Refrigerante guaraná 2L".
            // ILike = "contém", sem diferenciar maiúsculas/minúsculas; Unaccent = ignora acentos (recursos do PostgreSQL).
            // Ou o código de barras exato, vindo do leitor.
            var palavras = termo.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6).Select(SemCuringa).ToList();
            var porNome = consulta;
            foreach (var palavra in palavras)
                porNome = porNome.Where(p => EF.Functions.ILike(EF.Functions.Unaccent(p.Nome), EF.Functions.Unaccent($"%{palavra}%")));
            consulta = porNome.Union(consulta.Where(p => p.CodigoBarras == termo));
        }

        if (categoriaId is not null)
            consulta = consulta.Where(p => p.CategoriaId == categoriaId);

        if (estoqueBaixo)
            consulta = consulta.Where(p => p.EstoqueAtual <= p.EstoqueMinimo);

        var total = await consulta.CountAsync();

        // Com busca: primeiro os nomes que COMEÇAM com o que foi digitado ("arroz" → "Arroz…" antes de "Biscoito de arroz").
        var inicio = SemCuringa(termo.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "") + "%";
        var ordenada = termo.Length > 0
            ? consulta.OrderBy(p => !EF.Functions.ILike(EF.Functions.Unaccent(p.Nome), EF.Functions.Unaccent(inicio))).ThenBy(p => p.Nome)
            : consulta.OrderBy(p => p.Nome);
        var itens = await ordenada
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(ParaResposta)
            .ToListAsync();

        return new Pagina<ProdutoResponse>(itens, total, pagina, tamanho);
    }

    // GET /api/produtos/etiquetas?precoAlteradoDesde=2026-10-09&categoriaId=3&ids=1,2,3
    // Produtos para imprimir etiqueta de gôndola: os escolhidos (ids) e/ou os que mudaram de preço desde o dia.
    [HttpGet("etiquetas")]
    [Authorize(Roles = PodeEditar)]
    public async Task<List<EtiquetaResponse>> Etiquetas(DateOnly? precoAlteradoDesde, int? categoriaId, string? ids)
    {
        var consulta = db.Produtos.AsNoTracking().Where(p => p.Ativo);
        var lista = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => int.TryParse(t, out var n) ? n : 0).Where(n => n > 0).Take(2000).ToList();

        if (lista.Count > 0) consulta = consulta.Where(p => lista.Contains(p.Id));
        else if (precoAlteradoDesde is null && categoriaId is null) return [];
        if (precoAlteradoDesde is DateOnly dia)
        {
            var inicio = Services.Relogio.InicioDoDiaUtc(dia);
            consulta = consulta.Where(p => p.PrecoAlteradoEm >= inicio);
        }
        if (categoriaId is int c) consulta = consulta.Where(p => p.CategoriaId == c);

        return await consulta.OrderBy(p => p.Categoria!.Nome).ThenBy(p => p.Nome).Take(2000)
            .Select(p => new EtiquetaResponse(p.Id, p.Nome, p.CodigoBarras, p.Unidade, p.PrecoVenda, p.Categoria!.Nome, p.PrecoAlteradoEm))
            .ToListAsync();
    }

    // GET /api/produtos/balanca/etiqueta/2004200015997 → o PDV bipou a etiqueta da balança: qual produto e quanto.
    // Todos os perfis (o caixa usa). A regra do formato fica aqui, no servidor (uma só, para todas as telas).
    [HttpGet("balanca/etiqueta/{codigo}")]
    public async Task<ActionResult<EtiquetaBalancaResponse>> LerEtiquetaBalanca(string codigo, [FromServices] Seguranca.ContextoEmpresa contexto)
    {
        var empresa = await db.Empresas.AsNoTracking().FirstAsync(e => e.Id == contexto.EmpresaId);
        var leitura = Services.EtiquetaBalanca.Ler(codigo, empresa.BalancaDigitosCodigo, empresa.BalancaEtiqueta, out var erro);
        if (leitura is null) return BadRequest(new { mensagem = erro });

        var produto = await db.Produtos.AsNoTracking().Where(p => p.CodigoBalanca == leitura.CodigoProduto).Select(ParaResposta).FirstOrDefaultAsync();
        if (produto is null) return NotFound(new { mensagem = $"Nenhum produto com o código de balança {leitura.CodigoProduto}." });
        if (!produto.Ativo) return BadRequest(new { mensagem = $"\"{produto.Nome}\" está desativado." });
        if (leitura.Valor <= 0) return BadRequest(new { mensagem = "Etiqueta com valor zero." });

        if (empresa.BalancaEtiqueta == EtiquetaBalancaTipos.Peso)
            return new EtiquetaBalancaResponse(produto, leitura.Valor, null, leitura.Valor);

        // Etiqueta com o PREÇO: o peso é o preço ÷ preço do quilo (3 casas). Produto por unidade: tem que dar inteiro.
        if (produto.PrecoVenda <= 0) return BadRequest(new { mensagem = $"\"{produto.Nome}\" está sem preço de venda." });
        var quantidade = Math.Round(leitura.Valor / produto.PrecoVenda, 3);
        if (produto.Unidade is not (Unidades.Quilo or Unidades.Litro))
        {
            if (quantidade % 1 != 0)
                return BadRequest(new { mensagem = $"A etiqueta (R$ {leitura.Valor:N2}) não bate com o preço de \"{produto.Nome}\" (R$ {produto.PrecoVenda:N2} a unidade). Confira o preço na balança." });
        }
        return new EtiquetaBalancaResponse(produto, quantidade, leitura.Valor, null);
    }

    // GET /api/produtos/balanca/exportar → lista para cadastrar na balança (PLU; descrição; preço por kg).
    // Arquivo .csv separado por ";" (o programa da balança importa ou serve de conferência).
    [HttpGet("balanca/exportar")]
    [Authorize(Roles = PodeEditar)]
    public async Task<IActionResult> ExportarBalanca()
    {
        var itens = await db.Produtos.AsNoTracking().Where(p => p.Ativo && p.CodigoBalanca != null).OrderBy(p => p.CodigoBalanca)
            .Select(p => new { p.CodigoBalanca, p.Nome, p.Unidade, p.PrecoVenda, Categoria = p.Categoria!.Nome }).ToListAsync();
        var linhas = new List<string> { "PLU;Descricao;Unidade;Preco;Categoria" };
        linhas.AddRange(itens.Select(i => string.Join(';', i.CodigoBalanca, Csv(i.Nome), i.Unidade == Unidades.Quilo ? "KG" : "UN",
            i.PrecoVenda.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")), Csv(i.Categoria))));
        // Windows-1252: o programa das balanças (Windows) abre acentos certinho.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var bytes = System.Text.Encoding.GetEncoding(1252).GetBytes(string.Join("\r\n", linhas) + "\r\n");
        return File(bytes, "text/csv", $"balanca-produtos-{Services.Relogio.HojeBrasilia:yyyy-MM-dd}.csv");
    }

    // GET /api/produtos/balanca/proximo-codigo → sugere o próximo PLU livre (no formulário do produto)
    [HttpGet("balanca/proximo-codigo")]
    [Authorize(Roles = PodeEditar)]
    public async Task<int> ProximoCodigoBalanca() => (await db.Produtos.MaxAsync(p => p.CodigoBalanca) ?? 0) + 1;

    // "%" e "_" digitados pela pessoa são texto, não curinga do LIKE.
    private static string SemCuringa(string texto) => texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string Csv(string texto) => texto.Replace(";", ",").Replace("\"", "'");

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

        var produto = new Produto(); // estoque começa em zero → depois, registrar uma entrada
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

        if (request.FornecedorId is not null && !await db.Fornecedores.AnyAsync(f => f.Id == request.FornecedorId))
            ModelState.AddModelError(nameof(request.FornecedorId), "Fornecedor não encontrado.");

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!string.IsNullOrWhiteSpace(request.CodigoBarras) &&
            await db.Produtos.AnyAsync(p => p.CodigoBarras == request.CodigoBarras && p.Id != idAtual))
            return Conflict(new { mensagem = "Já existe um produto com este código de barras." });

        if (request.CodigoBalanca is int plu &&
            await db.Produtos.Where(p => p.CodigoBalanca == plu && p.Id != idAtual).Select(p => p.Nome).FirstOrDefaultAsync() is { } outro)
            return Conflict(new { mensagem = $"O código de balança {plu} já é do produto \"{outro}\"." });

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
        produto.FornecedorId = request.FornecedorId;
        produto.CodigoBalanca = request.CodigoBalanca;
        produto.Ncm = Vazio(request.Ncm);
        produto.Cest = Vazio(request.Cest);
        produto.Cfop = Vazio(request.Cfop) ?? "5102";
        produto.Origem = request.Origem;
        produto.SituacaoTributaria = Vazio(request.SituacaoTributaria);
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

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
            p.Ativo,
            p.FornecedorId,
            p.Fornecedor != null ? p.Fornecedor.Nome : null,
            p.CodigoBalanca,
            p.Ncm,
            p.Cest,
            p.Cfop,
            p.Origem,
            p.SituacaoTributaria);
}
