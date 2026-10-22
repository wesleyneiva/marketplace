using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Oficina: ordens de serviço (encordoamento, troca de grip). Todos os perfis da loja usam (o caixa recebe a raquete).
[ApiController]
[Route("api/ordens")]
[Authorize]
public class OrdensController(AppDbContext db, ClientesService clientes, EstoqueService estoque) : ControllerBase
{
    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // GET /api/ordens?situacao=abertas|prontas|entregues|todas&busca=joao&pagina=1&tamanho=25
    [HttpGet]
    public async Task<Pagina<OrdemResponse>> Listar(string situacao = "abertas", string? busca = null, int pagina = 1, int tamanho = 25)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 100);
        var q = db.OrdensServico.AsNoTracking();
        q = situacao switch
        {
            "prontas" => q.Where(o => o.Status == StatusOrdem.Pronta),
            "entregues" => q.Where(o => o.Status == StatusOrdem.Entregue),
            "canceladas" => q.Where(o => o.Status == StatusOrdem.Cancelada),
            "todas" => q,
            _ => q.Where(o => o.Status == StatusOrdem.Recebida || o.Status == StatusOrdem.EmAndamento),
        };
        foreach (var palavra in (busca ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5)
            .Select(t => t.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")))
        {
            var termo = $"%{palavra}%";
            q = q.Where(o => EF.Functions.ILike(EF.Functions.Unaccent(o.Cliente), EF.Functions.Unaccent(termo))
                || EF.Functions.ILike(o.Telefone ?? "", termo) || EF.Functions.ILike(EF.Functions.Unaccent(o.Raquete), EF.Functions.Unaccent(termo))
                || EF.Functions.ILike(o.Numero.ToString(), palavra));
        }
        var total = await q.CountAsync();
        var ordenada = situacao is "entregues" or "todas" or "canceladas"
            ? q.OrderByDescending(o => o.Numero)
            : q.OrderBy(o => o.Prazo).ThenBy(o => o.Numero);
        var itens = await Projetar(ordenada.Skip((pagina - 1) * tamanho).Take(tamanho)).ToListAsync();
        return new Pagina<OrdemResponse>(itens, total, pagina, tamanho);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrdemResponse>> Obter(int id) =>
        await Projetar(db.OrdensServico.AsNoTracking().Where(o => o.Id == id)).FirstOrDefaultAsync() is { } o ? o : NotFound();

    // GET /api/ordens/resumo → números do topo da tela e do painel.
    [HttpGet("resumo")]
    public async Task<ResumoOrdensResponse> Resumo()
    {
        var agora = DateTimeOffset.UtcNow;
        var inicioDoDia = new DateTimeOffset(Relogio.Hoje.ToDateTime(TimeOnly.MinValue), Relogio.Agora.Offset).ToUniversalTime();
        var abertas = await db.OrdensServico.AsNoTracking()
            .Where(o => o.Status == StatusOrdem.Recebida || o.Status == StatusOrdem.EmAndamento || o.Status == StatusOrdem.Pronta)
            .Select(o => new { o.Status, o.Prazo }).ToListAsync();
        var entreguesHoje = await db.OrdensServico.CountAsync(o => o.Status == StatusOrdem.Entregue && o.EntregueEm >= inicioDoDia);
        // Retrabalho do mês: quantas voltaram (garantia), quanto custou em corda e quantas foram entregues (para a proporção).
        var inicioMes = Relogio.InicioDoDiaUtc(new DateOnly(Relogio.Hoje.Year, Relogio.Hoje.Month, 1));
        var garantias = await db.OrdensServico.AsNoTracking()
            .Where(o => o.GarantiaDeId != null && o.Status != StatusOrdem.Cancelada && o.CriadaEm >= inicioMes)
            .Select(o => new { o.CustoGarantia, o.GarantiaDeId }).ToListAsync();
        var entreguesMes = await db.OrdensServico.CountAsync(o => o.Status == StatusOrdem.Entregue && o.GarantiaDeId == null && o.EntregueEm >= inicioMes);
        return new ResumoOrdensResponse(
            abertas.Count(o => o.Status == StatusOrdem.Recebida), abertas.Count(o => o.Status == StatusOrdem.EmAndamento),
            abertas.Count(o => o.Status == StatusOrdem.Pronta),
            abertas.Count(o => StatusOrdem.EmAberto(o.Status) && o.Prazo < agora), entreguesHoje,
            garantias.Count, garantias.Sum(g => g.CustoGarantia), entreguesMes, garantias.Select(g => g.GarantiaDeId).Distinct().Count());
    }

    // GET /api/ordens/historico?telefone=21999998888 → as últimas raquetes/cordas/tensões do cliente.
    [HttpGet("historico")]
    public async Task<List<HistoricoClienteResponse>> Historico(string telefone)
    {
        var tel = new string((telefone ?? "").Where(char.IsDigit).ToArray());
        if (tel.Length < 8) return [];
        return await db.OrdensServico.AsNoTracking().Where(o => o.Telefone == tel && o.Status != StatusOrdem.Cancelada)
            .OrderByDescending(o => o.CriadaEm).Take(5)
            .Select(o => new HistoricoClienteResponse(o.Cliente, o.Raquete, o.Corda, o.Tensao, o.CriadaEm)).ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<OrdemResponse>> Criar(OrdemRequest r)
    {
        var ordem = new OrdemServico { CriadaPorId = UsuarioId };
        Preencher(ordem, r);
        // Telefone de cliente: liga a OS ao cadastro (cria, se novo) e guarda raquete/corda/tensão para a próxima vez.
        ordem.ClienteId = await clientes.VincularDaOficinaAsync(ordem.Cliente, ordem.Telefone, ordem.Raquete, ordem.Corda, ordem.Tensao, UsuarioId);
        await OrdensServicoNumeracao.SalvarNovaAsync(db, ordem);
        return CreatedAtAction(nameof(Obter), new { id = ordem.Id }, await Projetar(db.OrdensServico.Where(o => o.Id == ordem.Id)).FirstAsync());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<OrdemResponse>> Editar(int id, OrdemRequest r)
    {
        var ordem = await db.OrdensServico.FirstOrDefaultAsync(o => o.Id == id);
        if (ordem is null) return NotFound();
        if (ordem.Status is StatusOrdem.Entregue or StatusOrdem.Cancelada)
            return BadRequest(new { mensagem = "Ordem entregue ou cancelada não muda." });
        Preencher(ordem, r);
        ordem.ClienteId = await clientes.VincularDaOficinaAsync(ordem.Cliente, ordem.Telefone, ordem.Raquete, ordem.Corda, ordem.Tensao, UsuarioId);
        await db.SaveChangesAsync();
        return await Projetar(db.OrdensServico.Where(o => o.Id == id)).FirstAsync();
    }

    // POST /api/ordens/5/status { "status": "Pronta" } → anda com a ordem (ou volta um passo, se clicou errado).
    [HttpPost("{id:int}/status")]
    public async Task<ActionResult<OrdemResponse>> MudarStatus(int id, StatusOrdemRequest r)
    {
        if (!StatusOrdem.Todos.Contains(r.Status)) return BadRequest(new { mensagem = "Situação inválida." });
        var ordem = await db.OrdensServico.FirstOrDefaultAsync(o => o.Id == id);
        if (ordem is null) return NotFound();
        OrdensServicoNumeracao.Mudar(ordem, r.Status);
        await db.SaveChangesAsync();
        return await Projetar(db.OrdensServico.Where(o => o.Id == id)).FirstAsync();
    }

    // POST /api/ordens/5/garantia → a raquete voltou (serviço ruim): OS nova ligada à original, sem cobrança; a corda nova
    // (se houver) sai do estoque como perda "Garantia da oficina", pelo custo.
    [HttpPost("{id:int}/garantia")]
    public async Task<ActionResult<OrdemResponse>> Garantia(int id, GarantiaRequest r)
    {
        var original = await db.OrdensServico.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (original is null) return NotFound();
        if (original.Status != StatusOrdem.Entregue) return BadRequest(new { mensagem = "Só uma ordem já entregue volta para garantia." });
        if (!MotivosGarantia.Todos.Contains(r.Motivo)) return BadRequest(new { mensagem = "Escolha o motivo do retrabalho." });
        Produto? corda = null;
        if (r.CordaProdutoId is int cordaId)
        {
            corda = await db.Produtos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == cordaId && p.Ativo && !p.Servico);
            if (corda is null) return BadRequest(new { mensagem = "Corda não encontrada." });
            if (corda.EstoqueAtual < 1) return BadRequest(new { mensagem = $"Sem {corda.Nome} em estoque: escolha outra corda." });
        }

        var ordem = new OrdemServico
        {
            Cliente = original.Cliente, Telefone = original.Telefone, ClienteId = original.ClienteId, Raquete = original.Raquete,
            Corda = corda?.Nome ?? original.Corda, Tensao = original.Tensao, Servico = $"Garantia: {original.Servico}",
            Valor = 0, Paga = true, Prazo = r.Prazo, CriadaPorId = UsuarioId,
            Observacao = string.IsNullOrWhiteSpace(r.Observacao) ? null : r.Observacao.Trim(),
            GarantiaDeId = original.Id, MotivoGarantia = r.Motivo, CordaGarantiaId = corda?.Id, CustoGarantia = corda?.PrecoCusto ?? 0,
        };
        await using var transacao = await db.Database.BeginTransactionAsync();
        await OrdensServicoNumeracao.SalvarNovaAsync(db, ordem);
        if (corda is not null)
        {
            try
            {
                await estoque.RegistrarPerdaAsync(corda.Id, 1, "Garantia da oficina", null,
                    $"Garantia da OS nº {original.Numero} ({r.Motivo.ToLowerInvariant()}) — OS nº {ordem.Numero}", UsuarioId);
            }
            catch (EstoqueException e) { return BadRequest(new { mensagem = e.Message }); }
        }
        await transacao.CommitAsync();
        return CreatedAtAction(nameof(Obter), new { id = ordem.Id }, await Projetar(db.OrdensServico.Where(o => o.Id == ordem.Id)).FirstAsync());
    }

    // POST /api/ordens/5/paga { } → marca como paga (cobrada no caixa).
    [HttpPost("{id:int}/paga")]
    public async Task<ActionResult<OrdemResponse>> MarcarPaga(int id, [FromQuery] int? vendaId = null)
    {
        var ordem = await db.OrdensServico.FirstOrDefaultAsync(o => o.Id == id);
        if (ordem is null) return NotFound();
        ordem.Paga = true;
        if (vendaId is not null) ordem.VendaId = vendaId;
        await db.SaveChangesAsync();
        return await Projetar(db.OrdensServico.Where(o => o.Id == id)).FirstAsync();
    }

    private static void Preencher(OrdemServico o, OrdemRequest r)
    {
        static string? Limpo(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        o.Cliente = r.Cliente.Trim();
        o.Telefone = Limpo(new string((r.Telefone ?? "").Where(char.IsDigit).ToArray()));
        o.Raquete = r.Raquete.Trim();
        o.Corda = Limpo(r.Corda);
        o.Tensao = ClientesService.Tensao(r.Tensao);
        o.Servico = r.Servico.Trim();
        o.Valor = Math.Round(r.Valor, 2);
        o.Paga = r.Paga;
        o.Prazo = r.Prazo;
        o.Observacao = Limpo(r.Observacao);
    }

    private IQueryable<OrdemResponse> Projetar(IQueryable<OrdemServico> q)
    {
        var agora = DateTimeOffset.UtcNow;
        return q.Select(o => new OrdemResponse(o.Id, o.Numero, o.Cliente, o.Telefone, o.Raquete, o.Corda, o.Tensao, o.Servico,
            o.Valor, o.Paga, o.VendaId, o.Observacao, o.Status, o.Prazo, o.CriadaEm, o.ProntaEm, o.EntregueEm,
            db.Users.Where(u => u.Id == o.CriadaPorId).Select(u => u.NomeCompleto).FirstOrDefault(),
            (o.Status == StatusOrdem.Recebida || o.Status == StatusOrdem.EmAndamento) && o.Prazo < agora, o.Automatica, o.ClienteId,
            o.GarantiaDe != null ? o.GarantiaDe.Numero : null, o.MotivoGarantia,
            db.OrdensServico.Count(g => g.GarantiaDeId == o.Id && g.Status != StatusOrdem.Cancelada)));
    }
}
