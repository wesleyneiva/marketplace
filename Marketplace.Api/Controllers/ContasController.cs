using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Contas a pagar: as parcelas das notas de fornecedor (criadas sozinhas pela entrada da NF-e) e as lançadas à mão.
[ApiController]
[Route("api/contas")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class ContasController(AppDbContext db) : ControllerBase
{
    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // GET /api/contas?situacao=abertas|vencidas|pagas|todas&busca=luz&categoriaId=3&fornecedorId=2&origem=nota|manual
    //   &de=2026-10-01&ate=2026-10-31&pagina=1&tamanho=25
    // Período: na aba "pagas" é a data do pagamento; nas outras, o vencimento. Sem período, pagas/todas mostram só as
    // pagas nos últimos 90 dias (com período, qualquer época).
    [HttpGet]
    public async Task<ContasPagina> Listar(
        string situacao = "abertas", string? busca = null, int? categoriaId = null, int? fornecedorId = null,
        string? origem = null, DateOnly? de = null, DateOnly? ate = null, int pagina = 1, int tamanho = 25)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 100);
        var hoje = Relogio.Hoje;
        var semPeriodo = de is null && ate is null;
        var consulta = db.ContasPagar.AsNoTracking();
        consulta = situacao switch
        {
            "vencidas" => consulta.Where(c => c.PagaEm == null && c.Vencimento < hoje),
            "pagas" => consulta.Where(c => c.PagaEm != null && (!semPeriodo || c.PagaEm >= hoje.AddDays(-90))),
            "todas" => consulta.Where(c => c.PagaEm == null || !semPeriodo || c.PagaEm >= hoje.AddDays(-90)),
            _ => consulta.Where(c => c.PagaEm == null),
        };

        if (situacao == "pagas")
        {
            if (de is not null) consulta = consulta.Where(c => c.PagaEm >= de);
            if (ate is not null) consulta = consulta.Where(c => c.PagaEm <= ate);
        }
        else
        {
            if (de is not null) consulta = consulta.Where(c => c.Vencimento >= de);
            if (ate is not null) consulta = consulta.Where(c => c.Vencimento <= ate);
        }

        // Cada PALAVRA precisa aparecer na descrição, no documento, no fornecedor ou na observação (sem acento/maiúscula).
        foreach (var palavra in (busca ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6).Select(SemCuringa))
        {
            var termo = $"%{palavra}%";
            consulta = consulta.Where(c =>
                EF.Functions.ILike(EF.Functions.Unaccent(c.Descricao), EF.Functions.Unaccent(termo))
                || EF.Functions.ILike(c.Documento ?? "", termo)
                || EF.Functions.ILike(EF.Functions.Unaccent(c.Fornecedor!.Nome ?? ""), EF.Functions.Unaccent(termo))
                || EF.Functions.ILike(EF.Functions.Unaccent(c.Observacao ?? ""), EF.Functions.Unaccent(termo)));
        }
        if (categoriaId is not null) consulta = consulta.Where(c => c.CategoriaFinanceiraId == categoriaId);
        if (fornecedorId is not null) consulta = consulta.Where(c => c.FornecedorId == fornecedorId);
        if (origem == "nota") consulta = consulta.Where(c => c.NotaEntradaId != null);
        else if (origem == "manual") consulta = consulta.Where(c => c.NotaEntradaId == null);

        var total = await consulta.CountAsync();
        var valorTotal = await consulta.SumAsync(c => (decimal?)(c.ValorPago ?? c.Valor)) ?? 0;
        var ordenada = situacao == "pagas" ? consulta.OrderByDescending(c => c.PagaEm).ThenBy(c => c.Id)
            : consulta.OrderBy(c => c.Vencimento).ThenBy(c => c.Id);
        var itens = (await Projetar(ordenada.Skip((pagina - 1) * tamanho).Take(tamanho)).ToListAsync())
            .Select(c => Situar(c, hoje)).ToList();
        return new ContasPagina(itens, total, pagina, tamanho, valorTotal);
    }

    private static string SemCuringa(string texto) => texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // GET /api/contas/resumo → números do topo da tela (e do dashboard)
    [HttpGet("resumo")]
    public async Task<ResumoContasResponse> Resumo()
    {
        var hoje = Relogio.Hoje;
        var semana = hoje.AddDays(7);
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var abertas = await db.ContasPagar.AsNoTracking().Where(c => c.PagaEm == null)
            .Select(c => new { c.Vencimento, c.Valor }).ToListAsync();
        var pagoNoMes = await db.ContasPagar.AsNoTracking().Where(c => c.PagaEm >= inicioMes)
            .SumAsync(c => (decimal?)(c.ValorPago ?? c.Valor)) ?? 0;

        var vencidas = abertas.Where(c => c.Vencimento < hoje).ToList();
        var deHoje = abertas.Where(c => c.Vencimento == hoje).ToList();
        var semanaQueVem = abertas.Where(c => c.Vencimento > hoje && c.Vencimento <= semana).ToList();
        return new ResumoContasResponse(vencidas.Count, vencidas.Sum(c => c.Valor), deHoje.Count, deHoje.Sum(c => c.Valor),
            semanaQueVem.Count, semanaQueVem.Sum(c => c.Valor), abertas.Count, abertas.Sum(c => c.Valor), pagoNoMes);
    }

    // POST /api/contas → conta lançada à mão (com RepetirMeses > 1 vira uma por mês: aluguel, internet...)
    [HttpPost]
    public async Task<ActionResult<List<ContaPagarResponse>>> Criar(ContaPagarRequest r)
    {
        if (await FornecedorInvalidoAsync(r.FornecedorId)) return BadRequest(new { mensagem = "Fornecedor não encontrado." });
        var (categoria, erro) = await CategoriaAsync(r.CategoriaId, r.Descricao, null);
        if (erro is not null) return BadRequest(new { mensagem = erro });
        var contas = Enumerable.Range(0, r.RepetirMeses).Select(i => new ContaPagar
        {
            CategoriaFinanceiraId = categoria,
            Descricao = r.RepetirMeses > 1 ? $"{r.Descricao.Trim()} ({i + 1}/{r.RepetirMeses})" : r.Descricao.Trim(),
            Documento = Limpo(r.Documento), Vencimento = r.Vencimento.AddMonths(i), Valor = r.Valor,
            FornecedorId = r.FornecedorId, Observacao = Limpo(r.Observacao), CriadaPorId = UsuarioId,
        }).ToList();
        db.ContasPagar.AddRange(contas);
        await db.SaveChangesAsync();
        var ids = contas.Select(c => c.Id).ToList();
        var hoje = Relogio.Hoje;
        return (await Projetar(db.ContasPagar.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Vencimento)).ToListAsync())
            .Select(c => Situar(c, hoje)).ToList();
    }

    // PUT /api/contas/5 → corrigir (ex.: boleto renegociado). Conta paga não muda: desfaça o pagamento antes.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ContaPagarResponse>> Editar(int id, ContaPagarRequest r)
    {
        var conta = await db.ContasPagar.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (conta.PagaEm is not null) return BadRequest(new { mensagem = "Conta já paga: desfaça o pagamento para editar." });
        if (await FornecedorInvalidoAsync(r.FornecedorId)) return BadRequest(new { mensagem = "Fornecedor não encontrado." });
        var (categoria, erro) = await CategoriaAsync(r.CategoriaId, r.Descricao, conta.CategoriaFinanceiraId);
        if (erro is not null) return BadRequest(new { mensagem = erro });
        conta.CategoriaFinanceiraId = categoria;
        conta.Descricao = r.Descricao.Trim();
        conta.Documento = Limpo(r.Documento);
        conta.Vencimento = r.Vencimento;
        conta.Valor = r.Valor;
        conta.FornecedorId = r.FornecedorId;
        conta.Observacao = Limpo(r.Observacao);
        await db.SaveChangesAsync();
        return await UmaAsync(id);
    }

    // DELETE /api/contas/5 → só conta em aberto (lançada errada)
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var conta = await db.ContasPagar.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (conta.PagaEm is not null) return BadRequest(new { mensagem = "Conta já paga não pode ser excluída (desfaça o pagamento antes)." });
        db.ContasPagar.Remove(conta);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/contas/5/pagar
    [HttpPost("{id:int}/pagar")]
    public async Task<ActionResult<ContaPagarResponse>> Pagar(int id, PagarContaRequest r)
    {
        var conta = await db.ContasPagar.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (conta.PagaEm is not null) return BadRequest(new { mensagem = "Esta conta já foi paga." });
        if (r.PagaEm > Relogio.Hoje) return BadRequest(new { mensagem = "A data do pagamento não pode ser no futuro." });
        var deOnde = await db.ContasFinanceiras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ContaFinanceiraId);
        if (deOnde is null || !deOnde.Ativa) return BadRequest(new { mensagem = "Escolha de qual conta saiu o dinheiro." });
        if (r.PagaEm < deOnde.SaldoInicialEm)
            return BadRequest(new { mensagem = $"A data é anterior ao saldo inicial de \"{deOnde.Nome}\" ({deOnde.SaldoInicialEm:dd/MM/yyyy})." });
        if (await Financeiro.FaltaDinheiroAsync(db, deOnde.Id, r.ValorPago) is { } falta) return BadRequest(new { mensagem = falta });
        conta.PagaEm = r.PagaEm;
        conta.ValorPago = r.ValorPago;
        conta.FormaPagamento = Limpo(r.FormaPagamento);
        conta.PagaPorId = UsuarioId;
        // Conta + movimentos no livro juntos (tudo ou nada).
        await using var transacao = await db.Database.BeginTransactionAsync();
        await Financeiro.RegistrarPagamentoAsync(db, conta, deOnde.Id, UsuarioId);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await UmaAsync(id);
    }

    // POST /api/contas/5/desfazer-pagamento → marcou como paga sem querer
    [HttpPost("{id:int}/desfazer-pagamento")]
    public async Task<ActionResult<ContaPagarResponse>> DesfazerPagamento(int id)
    {
        var conta = await db.ContasPagar.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (await db.MovimentosFinanceiros.AnyAsync(m => m.ContaPagarId == id && m.Conciliado))
            return BadRequest(new { mensagem = "Este pagamento já foi conferido com o extrato do banco: desfaça a conciliação antes." });
        await using var transacao = await db.Database.BeginTransactionAsync();
        await Financeiro.DesfazerPagamentoAsync(db, id);
        conta.PagaEm = null;
        conta.ValorPago = null;
        conta.FormaPagamento = null;
        conta.PagaPorId = null;
        conta.ContaFinanceiraId = null;
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await UmaAsync(id);
    }

    // ------------------------------------------------------------------ apoio

    private async Task<ContaPagarResponse> UmaAsync(int id) =>
        Situar(await Projetar(db.ContasPagar.AsNoTracking().Where(c => c.Id == id)).FirstAsync(), Relogio.Hoje);

    private static IQueryable<ContaPagarResponse> Projetar(IQueryable<ContaPagar> q) =>
        q.Select(c => new ContaPagarResponse(c.Id, c.Descricao, c.Documento, c.Vencimento, c.Valor, "", 0,
            c.FornecedorId, c.Fornecedor!.Nome, c.NotaEntradaId, c.NotaEntrada!.Numero,
            c.PagaEm, c.ValorPago, c.FormaPagamento, c.PagaPor!.NomeCompleto, c.Observacao,
            c.CategoriaFinanceiraId, c.CategoriaFinanceira!.Nome, c.ContaFinanceiraId, c.ContaFinanceira!.Nome));

    private static ContaPagarResponse Situar(ContaPagarResponse c, DateOnly hoje)
    {
        var dias = c.Vencimento.DayNumber - hoje.DayNumber;
        var situacao = c.PagaEm is not null ? "Paga" : dias < 0 ? "Vencida" : dias == 0 ? "VenceHoje" : "Aberta";
        return c with { Situacao = situacao, DiasParaVencer = dias };
    }

    // Categoria da conta: a escolhida (tem de ser de SAÍDA e ativa) ou, vazia, reconhecida pela descrição.
    // "atual" = a que a conta já tinha (pode continuar mesmo se tiver sido desativada depois).
    private async Task<(int? Id, string? Erro)> CategoriaAsync(int? escolhida, string descricao, int? atual)
    {
        if (escolhida is null)
        {
            var categorias = await db.CategoriasFinanceiras.AsNoTracking().ToListAsync();
            return (Financeiro.SugerirCategoria(descricao, categorias, NaturezaFinanceira.Saida)
                    ?? await Financeiro.CategoriaAsync(db, ChavesFinanceiras.OutrasDespesas), null);
        }
        var c = await db.CategoriasFinanceiras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == escolhida);
        if (c is null) return (null, "Categoria não encontrada.");
        if (c.Natureza != NaturezaFinanceira.Saida) return (null, $"\"{c.Nome}\" é categoria de entrada: conta a pagar precisa de categoria de saída.");
        if (!c.Ativa && c.Id != atual) return (null, $"A categoria \"{c.Nome}\" está desativada.");
        return (c.Id, null);
    }

    private async Task<bool> FornecedorInvalidoAsync(int? id) => id is int f && !await db.Fornecedores.AnyAsync(x => x.Id == f);

    private static string? Limpo(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
