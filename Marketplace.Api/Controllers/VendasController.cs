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
[Route("api/vendas")]
[Authorize]
public class VendasController(VendaService vendas, CaixaService caixa, AppDbContext db, FiadoService fiado, AutorizacaoService autorizacoes) : ControllerBase
{
    private const string Gerencia = $"{Perfis.Administrador},{Perfis.Gerente}";
    private string UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool EhGerencia => User.IsInRole(Perfis.Administrador) || User.IsInRole(Perfis.Gerente);

    // POST /api/vendas → finaliza uma venda no MEU caixa aberto.
    [HttpPost]
    public async Task<ActionResult<VendaResponse>> Finalizar(NovaVendaRequest pedido)
    {
        try
        {
            var venda = await vendas.FinalizarAsync(UsuarioId, podeDescontoLivre: EhGerencia, pedido);
            return CreatedAtAction(nameof(Obter), new { id = venda.Id }, await MontarCupomAsync(venda.Id));
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

    // GET /api/vendas/5 → o "cupom". Caixa só vê as vendas que ele mesmo fez.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendaResponse>> Obter(int id)
    {
        var dono = await db.Vendas.Where(v => v.Id == id).Select(v => v.UsuarioId).FirstOrDefaultAsync();
        if (dono is null) return NotFound();
        if (!EhGerencia && dono != UsuarioId) return Forbid();
        return await MontarCupomAsync(id);
    }

    // GET /api/vendas?data=2026-10-08&sessaoId=3&caixa=2&forma=Pix&situacao=Cancelada&pagina=1
    // Gerência vê tudo; Caixa vê só as vendas do caixa aberto dele. Os totais valem para o filtro inteiro (sem a situação).
    [HttpGet]
    public async Task<ListaVendasResponse> Listar(DateOnly? data, int? sessaoId, [FromQuery(Name = "caixa")] int? numeroCaixa,
        string? forma, string? situacao, int pagina = 1, int tamanho = 20)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 100);
        var consulta = db.Vendas.AsNoTracking();

        if (!EhGerencia)
        {
            var minha = await caixa.ObterSessaoAbertaAsync(UsuarioId);
            consulta = consulta.Where(v => v.SessaoCaixaId == (minha != null ? minha.Id : -1));
        }
        if (sessaoId is not null) consulta = consulta.Where(v => v.SessaoCaixaId == sessaoId);
        if (data is not null)
        {
            var (inicio, fim) = DiaEmUtc(data.Value);
            consulta = consulta.Where(v => v.DataHora >= inicio && v.DataHora < fim);
        }
        if (Enum.TryParse<FormaPagamento>(forma, true, out var f)) consulta = consulta.Where(v => v.Pagamentos.Any(p => p.Forma == f));

        var caixas = await consulta.Select(v => v.SessaoCaixa!.NumeroCaixa).Distinct().OrderBy(n => n).ToListAsync();
        if (numeroCaixa is not null) consulta = consulta.Where(v => v.SessaoCaixa!.NumeroCaixa == numeroCaixa);

        var totais = await consulta.GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key, Quantidade = g.Count(), Valor = g.Sum(v => v.Total) }).ToListAsync();
        var concluidas = totais.FirstOrDefault(t => t.Status == StatusVenda.Concluida);
        var canceladas = totais.FirstOrDefault(t => t.Status == StatusVenda.Cancelada);

        if (Enum.TryParse<StatusVenda>(situacao, true, out var st)) consulta = consulta.Where(v => v.Status == st);
        var total = await consulta.CountAsync();
        var itens = await consulta.OrderByDescending(v => v.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(v => new VendaListaResponse(
                v.Id, v.DataHora, v.SessaoCaixa!.NumeroCaixa, v.Usuario!.NomeCompleto, v.Status.ToString(),
                v.Itens.Count, v.Total,
                string.Join(" + ", v.Pagamentos.Select(p => p.Forma.ToString())),
                v.Pagamentos.Any(p => p.CobrancaPixId != null), v.Origem.ToString()))
            .ToListAsync();

        var qtd = concluidas?.Quantidade ?? 0;
        var vendido = concluidas?.Valor ?? 0;
        return new ListaVendasResponse(itens, total, pagina, tamanho, qtd, vendido, qtd == 0 ? 0 : Math.Round(vendido / qtd, 2),
            canceladas?.Quantidade ?? 0, canceladas?.Valor ?? 0, caixas);
    }

    // GET /api/vendas/5/estorno → o que acontece com o dinheiro se cancelar (a tela mostra antes de confirmar).
    [HttpGet("{id:int}/estorno")]
    public async Task<ActionResult<EstornoPreviaResponse>> Estorno(int id)
    {
        try
        {
            return await vendas.PreviaEstornoAsync(id, UsuarioId, EhGerencia);
        }
        catch (EstoqueException e)
        {
            return BadRequest(new { mensagem = e.Message });
        }
    }

    // POST /api/vendas/5/cancelar → cancela a venda paga e estorna. Gerente cancela direto; o operador de caixa,
    // só as vendas do caixa aberto dele e com a senha de um gerente (é o famoso "chama o gerente!").
    [HttpPost("{id:int}/cancelar")]
    public async Task<ActionResult<VendaResponse>> Cancelar(int id, CancelarVendaRequest r)
    {
        try
        {
            // Antes da senha, confere se a venda pode ser cancelada (não gasta tentativa de senha à toa).
            var previa = await vendas.PreviaEstornoAsync(id, UsuarioId, EhGerencia);
            if (previa.Bloqueio is not null) return BadRequest(new { mensagem = previa.Bloqueio });
            var autorizou = await autorizacoes.ConferirAsync(UsuarioId, EhGerencia, r.Autorizacao);
            await vendas.CancelarAsync(id, UsuarioId, EhGerencia, autorizou, r.Motivo, r.Pix);
            return await MontarCupomAsync(id);
        }
        catch (AutorizacaoException e)
        {
            return BadRequest(new { mensagem = e.Message, autorizacao = true });
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

    // GET /api/vendas/cancelamentos?data=2026-10-16 → itens e compras tirados no caixa e vendas canceladas, com quem autorizou.
    [HttpGet("cancelamentos")]
    [Authorize(Roles = Gerencia)]
    public async Task<List<AutorizacaoResponse>> Cancelamentos(DateOnly? data, [FromQuery(Name = "caixa")] int? numeroCaixa)
    {
        var (inicio, fim) = DiaEmUtc(data ?? Relogio.Hoje);
        var consulta = db.AutorizacoesGerente.AsNoTracking().Where(a => a.DataHora >= inicio && a.DataHora < fim);
        if (numeroCaixa is not null) consulta = consulta.Where(a => a.SessaoCaixa!.NumeroCaixa == numeroCaixa);
        return await consulta.OrderByDescending(a => a.Id)
            .Select(a => new AutorizacaoResponse(a.Id, a.DataHora, a.Acao.ToString(), a.SessaoCaixa != null ? a.SessaoCaixa.NumeroCaixa : null,
                a.Operador!.NomeCompleto, a.AutorizadoPor!.NomeCompleto, a.Motivo, a.Descricao, a.Valor, a.VendaId))
            .ToListAsync();
    }

    // GET /api/vendas/resumo?data=2026-10-08 → números do dia para o dashboard.
    [HttpGet("resumo")]
    [Authorize(Roles = Gerencia)]
    public async Task<ResumoVendasResponse> Resumo(DateOnly? data)
    {
        var dia = data ?? Relogio.Hoje;
        var (inicio, fim) = DiaEmUtc(dia);
        var doDia = db.Vendas.AsNoTracking()
            .Where(v => v.Status == StatusVenda.Concluida && v.DataHora >= inicio && v.DataHora < fim);

        var quantidade = await doDia.CountAsync();
        var total = await doDia.SumAsync(v => (decimal?)v.Total) ?? 0;
        var troco = await doDia.SumAsync(v => (decimal?)v.Troco) ?? 0;
        var desconto = await doDia.SumAsync(v => (decimal?)v.Desconto) ?? 0;
        var custo = await doDia.SelectMany(v => v.Itens).SumAsync(i => (decimal?)(i.Quantidade * i.CustoUnitario)) ?? 0;

        var porForma = await doDia.SelectMany(v => v.Pagamentos)
            .GroupBy(p => p.Forma).Select(g => new { g.Key, Valor = g.Sum(p => p.Valor) }).ToListAsync();
        var formas = Enum.GetValues<FormaPagamento>()
            .Select(f => new TotalPorForma(f.ToString(),
                (porForma.FirstOrDefault(x => x.Key == f)?.Valor ?? 0) - (f == FormaPagamento.Dinheiro ? troco : 0)))
            .ToList();

        // Agrupa por hora de Brasília (os horários estão guardados em UTC, 3 h à frente).
        var horarios = await doDia.Select(v => new { v.DataHora, v.Total }).ToListAsync();
        var porHora = horarios
            .GroupBy(v => v.DataHora.ToOffset(TimeSpan.FromHours(-3)).Hour)
            .Select(g => new VendasPorHora(g.Key, g.Count(), g.Sum(v => v.Total)))
            .OrderBy(h => h.Hora)
            .ToList();

        return new ResumoVendasResponse(dia, quantidade, total,
            quantidade == 0 ? 0 : Math.Round(total / quantidade, 2),
            Math.Round(total - custo, 2), formas, porHora);
    }

    private async Task<VendaResponse> MontarCupomAsync(int id)
    {
        var cupom = await db.Vendas.AsNoTracking().Where(v => v.Id == id)
            .Select(v => new VendaResponse(
                v.Id, v.DataHora, v.SessaoCaixa!.NumeroCaixa, v.Usuario!.NomeCompleto, v.Status.ToString(),
                v.Subtotal, v.Desconto, v.Total, v.ValorPago, v.Troco,
                v.Itens.OrderBy(i => i.Id).Select(i => new ItemVendaResponse(i.ProdutoId, i.Descricao, i.Unidade, i.Quantidade, i.PrecoUnitario, i.Total)).ToList(),
                v.Pagamentos.OrderBy(p => p.Id).Select(p => new PagamentoResponse(p.Forma.ToString(), p.Valor, p.CobrancaPixId != null)).ToList(),
                v.MotivoCancelamento, null,
                v.Status == StatusVenda.Cancelada
                    ? new CancelamentoCupom(v.CanceladaEm, v.CanceladaPor!.NomeCompleto,
                        db.AutorizacoesGerente.Where(a => a.VendaId == v.Id && a.Acao == AcaoAutorizada.CancelarVenda)
                            .OrderByDescending(a => a.Id).Select(a => a.AutorizadoPor!.NomeCompleto).FirstOrDefault())
                    : null))
            .FirstAsync();

        // Cliente identificado: no fiado, o saldo devedor de AGORA e o vencimento desta compra (para assinar no cupom).
        var cliente = await db.Vendas.Where(v => v.Id == id && v.ClienteId != null)
            .Select(v => new { Id = v.ClienteId!.Value, v.Cliente!.Nome, v.Cliente.Apelido }).FirstOrDefaultAsync();
        if (cliente is null) return cupom;
        var compra = await db.LancamentosFiado.AsNoTracking()
            .Where(l => l.VendaId == id && l.Tipo == TipoLancamentoFiado.Compra).Select(l => l.Vencimento).FirstOrDefaultAsync();
        var noFiado = cupom.Pagamentos.Any(p => p.Forma == nameof(FormaPagamento.Fiado));
        return cupom with
        {
            Cliente = new ClienteCupom(cliente.Nome, cliente.Apelido,
                noFiado ? (await fiado.SituacaoAsync(cliente.Id)).Saldo : null, noFiado ? compra : null),
        };
    }

    private static (DateTimeOffset Inicio, DateTimeOffset Fim) DiaEmUtc(DateOnly dia) =>
        (Relogio.InicioDoDiaUtc(dia), Relogio.InicioDoDiaUtc(dia.AddDays(1)));
}
