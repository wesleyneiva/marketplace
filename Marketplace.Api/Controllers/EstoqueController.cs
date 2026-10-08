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

    // GET /api/estoque/validades?dias=7 → lotes com saldo que vencem até daqui a N dias (inclui os vencidos).
    [HttpGet("validades")]
    public Task<List<LoteResponse>> Validades(int dias = 7) =>
        ConsultarLotes(db.Lotes.Where(l => l.DataValidade <= Relogio.HojeBrasilia.AddDays(Math.Clamp(dias, 0, 365))));

    // GET /api/estoque/produtos/12/lotes → todos os lotes com saldo de um produto.
    [HttpGet("produtos/{produtoId:int}/lotes")]
    public Task<List<LoteResponse>> LotesDoProduto(int produtoId) =>
        ConsultarLotes(db.Lotes.Where(l => l.ProdutoId == produtoId));

    private static async Task<List<LoteResponse>> ConsultarLotes(IQueryable<LoteValidade> consulta)
    {
        var hoje = Relogio.HojeBrasilia;
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
