using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Estoque: só Administrador e Gerente. O controller só recebe e responde;
// as regras ficam no EstoqueService (que o PDV também vai usar).
[ApiController]
[Route("api/estoque")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class EstoqueController(AppDbContext db, EstoqueService estoque) : ControllerBase
{
    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // POST /api/estoque/entradas
    [HttpPost("entradas")]
    public Task<ActionResult<MovimentacaoResponse>> Entrada(EntradaRequest r) =>
        Executar(() => estoque.RegistrarEntradaAsync(r.ProdutoId, r.Quantidade, r.CustoUnitario, r.Validade, r.Observacao, UsuarioId));

    // POST /api/estoque/perdas
    [HttpPost("perdas")]
    public Task<ActionResult<MovimentacaoResponse>> Perda(PerdaRequest r) =>
        Executar(() => estoque.RegistrarPerdaAsync(r.ProdutoId, r.Quantidade, r.Motivo, r.LoteId, r.Observacao, UsuarioId));

    // POST /api/estoque/ajustes
    [HttpPost("ajustes")]
    public Task<ActionResult<MovimentacaoResponse>> Ajuste(AjusteRequest r) =>
        Executar(() => estoque.RegistrarAjusteAsync(r.ProdutoId, r.QuantidadeContada, r.Validade, r.Observacao, UsuarioId));

    // GET /api/estoque/motivos-perda
    [HttpGet("motivos-perda")]
    public string[] MotivosPerda() => EstoqueService.MotivosPerda;

    // GET /api/estoque/movimentacoes?produtoId=12&tipo=Perda&pagina=1&tamanho=20
    [HttpGet("movimentacoes")]
    public async Task<Pagina<MovimentacaoResponse>> Movimentacoes(
        int? produtoId, TipoMovimentacao? tipo, DateOnly? de, DateOnly? ate, int pagina = 1, int tamanho = 20)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 100);

        var consulta = db.Movimentacoes.AsNoTracking();
        if (produtoId is not null) consulta = consulta.Where(m => m.ProdutoId == produtoId);
        if (tipo is not null) consulta = consulta.Where(m => m.Tipo == tipo);
        if (de is not null)
        {
            var inicio = Relogio.InicioDoDiaUtc(de.Value);
            consulta = consulta.Where(m => m.DataHora >= inicio);
        }
        if (ate is not null)
        {
            var fim = Relogio.InicioDoDiaUtc(ate.Value.AddDays(1));
            consulta = consulta.Where(m => m.DataHora < fim);
        }

        var total = await consulta.CountAsync();
        var itens = await consulta
            .OrderByDescending(m => m.DataHora).ThenByDescending(m => m.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(ParaResposta)
            .ToListAsync();

        return new Pagina<MovimentacaoResponse>(itens, total, pagina, tamanho);
    }

    // GET /api/estoque/posicao?busca=arroz&categoriaId=3&situacao=zerado&ordem=valor&pagina=1&tamanho=50
    // Posição do estoque: os totais (valor a custo e a venda, zerados, negativos, abaixo do mínimo) e a parte de cada
    // categoria são sempre da loja inteira (só produtos ativos); a lista segue os filtros.
    // situacao: comSaldo | semEstoque (zerado ou negativo) | zerado | negativo | baixo (baixo = como no painel: atual <= mínimo). ordem: nome | valor | quantidade.
    [HttpGet("posicao")]
    public async Task<PosicaoEstoqueResponse> Posicao(
        string? busca, int? categoriaId, string? situacao, string? ordem, int pagina = 1, int tamanho = 50)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 100);

        var ativos = db.Produtos.AsNoTracking().Where(p => p.Ativo && !p.Servico); // serviço não tem estoque

        var porCategoria = await ativos
            .GroupBy(p => new { p.CategoriaId, p.Categoria!.Nome })
            .Select(g => new
            {
                g.Key.CategoriaId, g.Key.Nome,
                Produtos = g.Count(),
                ComSaldo = g.Count(p => p.EstoqueAtual > 0),
                Zerados = g.Count(p => p.EstoqueAtual == 0),
                Negativos = g.Count(p => p.EstoqueAtual < 0),
                Baixo = g.Count(p => !p.Servico && p.EstoqueAtual <= p.EstoqueMinimo),
                ValorCusto = g.Sum(p => p.EstoqueAtual > 0 ? p.EstoqueAtual * p.PrecoCusto : 0),
                ValorVenda = g.Sum(p => p.EstoqueAtual > 0 ? p.EstoqueAtual * p.PrecoVenda : 0),
            })
            .ToListAsync();

        var totais = new PosicaoTotais(
            porCategoria.Sum(c => c.Produtos), porCategoria.Sum(c => c.ComSaldo), porCategoria.Sum(c => c.Zerados),
            porCategoria.Sum(c => c.Negativos), porCategoria.Sum(c => c.Baixo),
            Math.Round(porCategoria.Sum(c => c.ValorCusto), 2), Math.Round(porCategoria.Sum(c => c.ValorVenda), 2));
        var categorias = porCategoria
            .OrderByDescending(c => c.ValorCusto).ThenBy(c => c.Nome)
            .Select(c => new PosicaoCategoria(c.CategoriaId, c.Nome, c.Produtos, Math.Round(c.ValorCusto, 2), Math.Round(c.ValorVenda, 2)))
            .ToList();

        // A lista: mesma busca da tela Produtos (palavras em qualquer ordem, sem acento, ou o código de barras exato).
        var consulta = ativos;
        var termo = busca?.Trim() ?? "";
        if (termo.Length > 0)
        {
            var palavras = termo.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6)
                .Select(t => t.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")).ToList();
            var porNome = consulta;
            foreach (var palavra in palavras)
                porNome = porNome.Where(p => EF.Functions.ILike(EF.Functions.Unaccent(p.Nome), EF.Functions.Unaccent($"%{palavra}%")));
            consulta = porNome.Union(consulta.Where(p => p.CodigoBarras == termo));
        }
        if (categoriaId is not null) consulta = consulta.Where(p => p.CategoriaId == categoriaId);
        consulta = situacao switch
        {
            "comSaldo" => consulta.Where(p => p.EstoqueAtual > 0),
            "semEstoque" => consulta.Where(p => p.EstoqueAtual <= 0),
            "zerado" => consulta.Where(p => p.EstoqueAtual == 0),
            "negativo" => consulta.Where(p => p.EstoqueAtual < 0),
            "baixo" => consulta.Where(p => !p.Servico && p.EstoqueAtual <= p.EstoqueMinimo),
            _ => consulta,
        };

        var total = await consulta.CountAsync();
        var ordenada = ordem switch
        {
            "valor" => consulta.OrderByDescending(p => p.EstoqueAtual > 0 ? p.EstoqueAtual * p.PrecoCusto : 0).ThenBy(p => p.Nome),
            "quantidade" => consulta.OrderBy(p => p.EstoqueAtual).ThenBy(p => p.Nome),
            _ => consulta.OrderBy(p => p.Nome),
        };
        var linhas = await ordenada
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(p => new
            {
                p.Id, p.Nome, p.CodigoBarras, Categoria = p.Categoria!.Nome, p.Unidade,
                p.EstoqueAtual, p.EstoqueMinimo, p.PrecoCusto, p.PrecoVenda,
            })
            .ToListAsync();

        var itens = linhas.Select(p =>
        {
            var saldo = Math.Max(p.EstoqueAtual, 0);
            var sit = p.EstoqueAtual < 0 ? "Negativo" : p.EstoqueAtual == 0 ? "Zerado" : p.EstoqueAtual <= p.EstoqueMinimo ? "Baixo" : "Ok";
            return new PosicaoItem(p.Id, p.Nome, p.CodigoBarras, p.Categoria, p.Unidade, p.EstoqueAtual, p.EstoqueMinimo,
                p.PrecoCusto, p.PrecoVenda, Math.Round(saldo * p.PrecoCusto, 2), Math.Round(saldo * p.PrecoVenda, 2), sit);
        }).ToList();

        return new PosicaoEstoqueResponse(totais, categorias, new Pagina<PosicaoItem>(itens, total, pagina, tamanho));
    }

    // GET /api/estoque/validades?dias=7 → lotes com saldo que vencem até daqui a N dias (inclui os vencidos).
    [HttpGet("validades")]
    public Task<List<LoteResponse>> Validades(int dias = 7) =>
        ConsultarLotes(db.Lotes.Where(l => l.DataValidade <= Relogio.Hoje.AddDays(Math.Clamp(dias, 0, 365))));

    // GET /api/estoque/produtos/12/lotes → todos os lotes com saldo de um produto.
    [HttpGet("produtos/{produtoId:int}/lotes")]
    public Task<List<LoteResponse>> LotesDoProduto(int produtoId) =>
        ConsultarLotes(db.Lotes.Where(l => l.ProdutoId == produtoId));

    private static async Task<List<LoteResponse>> ConsultarLotes(IQueryable<LoteValidade> consulta)
    {
        var hoje = Relogio.Hoje;
        var lotes = await consulta.AsNoTracking()
            .Where(l => l.QuantidadeAtual > 0 && l.Produto!.Ativo)
            .OrderBy(l => l.DataValidade).ThenBy(l => l.Produto!.Nome)
            .Select(l => new
            {
                l.Id, l.ProdutoId, Produto = l.Produto!.Nome, Categoria = l.Produto.Categoria!.Nome,
                l.Produto.Unidade, l.DataValidade, l.QuantidadeAtual, l.Produto.PrecoCusto,
            })
            .ToListAsync();

        // Parte da conta é feita em C# (depois de buscar no banco): dias restantes e situação.
        return lotes.Select(l =>
        {
            var diasRestantes = l.DataValidade.DayNumber - hoje.DayNumber;
            var situacao = diasRestantes < 0 ? "Vencido" : diasRestantes == 0 ? "Vence hoje" : diasRestantes <= 7 ? "Vencendo" : "No prazo";
            // Valor em risco = quanto se perde (a preço de custo) se esse lote for para o lixo.
            return new LoteResponse(l.Id, l.ProdutoId, l.Produto, l.Categoria, l.Unidade, l.DataValidade,
                diasRestantes, situacao, l.QuantidadeAtual, Math.Round(l.QuantidadeAtual * l.PrecoCusto, 2));
        }).ToList();
    }

    // Executa a operação e traduz erros de regra de negócio em 400/409 com mensagem.
    // Uma operação pode gerar várias movimentações (uma por lote, no FEFO): a resposta é o RESUMO
    // delas (quantidade somada, estoque antes da primeira → depois da última).
    private async Task<ActionResult<MovimentacaoResponse>> Executar(Func<Task<List<MovimentacaoEstoque>>> operacao)
    {
        try
        {
            var movs = await operacao();
            var ids = movs.Select(m => m.Id).ToList();
            var linhas = await db.Movimentacoes.AsNoTracking().Where(x => ids.Contains(x.Id))
                .OrderBy(x => x.Id).Select(ParaResposta).ToListAsync();

            var resumo = linhas[^1] with
            {
                Quantidade = linhas.Sum(l => l.Quantidade),
                EstoqueAnterior = linhas[0].EstoqueAnterior,
            };
            return Created($"/api/estoque/movimentacoes/{resumo.Id}", resumo);
        }
        catch (ConflitoEstoqueException e)
        {
            return Conflict(new { mensagem = e.Message });
        }
        catch (EstoqueException e)
        {
            return BadRequest(new { mensagem = e.Message });
        }
    }

    private static readonly System.Linq.Expressions.Expression<Func<MovimentacaoEstoque, MovimentacaoResponse>> ParaResposta =
        x => new MovimentacaoResponse(
            x.Id, x.DataHora, x.ProdutoId, x.Produto!.Nome, x.Produto.Unidade, x.Tipo.ToString(),
            x.Quantidade, x.EstoqueAnterior, x.EstoquePosterior, x.CustoUnitario, x.Motivo, x.Observacao,
            x.Lote != null ? x.Lote.DataValidade : null,
            x.Usuario != null ? x.Usuario.NomeCompleto : "Sistema",
            x.VendaId);
}
