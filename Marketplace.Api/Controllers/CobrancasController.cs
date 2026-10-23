using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Área do dono: planos, mensalidades e regras de cobrança. Só o dono da plataforma, só pela rede interna.
[ApiController]
[Route("api/plataforma")]
[DonoDaPlataforma]
public class CobrancasController(AppDbContext db, CobrancasService cobrancas) : ControllerBase
{
    // ------------------------------------------------------------------ planos

    [HttpGet("planos")]
    public async Task<List<PlanoResponse>> Planos()
    {
        var uso = await db.Empresas.Where(e => e.PlanoId != null && !e.Plataforma).GroupBy(e => e.PlanoId)
            .Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key!.Value, x => x.Total);
        return (await db.Planos.AsNoTracking().OrderByDescending(p => p.Ativo).ThenBy(p => p.PrecoMensal).ToListAsync())
            .Select(p => new PlanoResponse(p.Id, p.Nome, p.Descricao, p.PrecoMensal, p.CaixasIncluidos, p.PrecoCaixaExtra, p.Ativo,
                uso.GetValueOrDefault(p.Id))).ToList();
    }

    [HttpPost("planos")]
    public async Task<IActionResult> CriarPlano(PlanoRequest r)
    {
        if (await db.Planos.AnyAsync(p => p.Nome == r.Nome.Trim())) return BadRequest(new { mensagem = "Já existe um plano com esse nome." });
        var plano = new Plano();
        Aplicar(plano, r);
        db.Planos.Add(plano);
        await db.SaveChangesAsync();
        return Ok(new { plano.Id });
    }

    // Mudar o preço vale para as PRÓXIMAS mensalidades (as já geradas ficam como estão).
    [HttpPut("planos/{id:int}")]
    public async Task<IActionResult> EditarPlano(int id, PlanoRequest r)
    {
        var plano = await db.Planos.FirstOrDefaultAsync(p => p.Id == id);
        if (plano is null) return NotFound();
        if (await db.Planos.AnyAsync(p => p.Id != id && p.Nome == r.Nome.Trim())) return BadRequest(new { mensagem = "Já existe um plano com esse nome." });
        Aplicar(plano, r);
        await db.SaveChangesAsync();
        return Ok(new { plano.Id });
    }

    private static void Aplicar(Plano p, PlanoRequest r)
    {
        p.Nome = r.Nome.Trim();
        p.Descricao = string.IsNullOrWhiteSpace(r.Descricao) ? null : r.Descricao.Trim();
        p.PrecoMensal = Math.Round(r.PrecoMensal, 2);
        p.CaixasIncluidos = r.CaixasIncluidos;
        p.PrecoCaixaExtra = Math.Round(r.PrecoCaixaExtra, 2);
        p.Ativo = r.Ativo;
    }

    // ------------------------------------------------------------------ mensalidades

    // GET /api/plataforma/faturas?situacao=abertas|atrasadas|pagas|todas&clienteId=5
    [HttpGet("faturas")]
    public async Task<List<FaturaResponse>> Faturas(string? situacao = "abertas", int? clienteId = null)
    {
        var hoje = Relogio.Hoje;
        var q = db.Faturas.AsNoTracking().Include(f => f.Cliente).AsQueryable();
        if (clienteId is not null) q = q.Where(f => f.ClienteId == clienteId);
        q = situacao switch
        {
            "abertas" => q.Where(f => f.Status == StatusFatura.Aberta),
            "atrasadas" => q.Where(f => f.Status == StatusFatura.Aberta && f.Vencimento < hoje),
            "pagas" => q.Where(f => f.Status == StatusFatura.Paga),
            _ => q,
        };
        var lista = await q.OrderBy(f => f.Status == StatusFatura.Aberta ? 0 : 1).ThenBy(f => f.Vencimento).ThenBy(f => f.Id)
            .Take(500).ToListAsync();
        if (situacao is "pagas" or "todas") lista = lista.OrderByDescending(f => f.Vencimento).ToList();
        return lista.Select(f => Resposta(f, hoje)).ToList();
    }

    // GET /api/plataforma/faturas/proximas → a próxima mensalidade de cada cliente, que ainda não apareceu
    [HttpGet("faturas/proximas")]
    public Task<List<ProximaMensalidade>> Proximas() => cobrancas.ProximasAsync(Relogio.Hoje);

    // POST /api/plataforma/faturas/gerar → roda agora a rotina das mensalidades (ela também roda sozinha de hora em hora)
    [HttpPost("faturas/gerar")]
    public async Task<object> Gerar(int? clienteId = null)
    {
        var novas = await cobrancas.GerarAsync(Relogio.Hoje, clienteId);
        await cobrancas.SincronizarAsync(Relogio.Hoje, clienteId);
        var bloqueados = clienteId is null ? await cobrancas.BloquearAtrasadosAsync(Relogio.Hoje) : 0;
        return new { novas, bloqueados };
    }

    // POST /api/plataforma/faturas → mensalidade avulsa (ex.: cobrar um mês fora da rotina, ou um serviço)
    [HttpPost("faturas")]
    public async Task<IActionResult> Criar(NovaFaturaRequest r)
    {
        var cliente = await db.Empresas.Include(e => e.Plano).FirstOrDefaultAsync(e => e.Id == r.ClienteId && !e.Plataforma);
        if (cliente is null) return BadRequest(new { mensagem = "Cliente não encontrado." });
        var competencia = new DateOnly(r.Competencia.Year, r.Competencia.Month, 1);
        if (await db.Faturas.AnyAsync(f => f.ClienteId == cliente.Id && f.Mensalidade && f.Competencia == competencia && f.Status != StatusFatura.Cancelada))
            return BadRequest(new { mensagem = "Este cliente já tem mensalidade nesse mês (cancele a outra antes, se for o caso)." });
        var valor = r.Valor ?? CobrancasService.ValorMensal(cliente, cliente.Plano);
        if (valor <= 0) return BadRequest(new { mensagem = "Informe o valor (o cliente não tem plano)." });
        var fatura = new Fatura
        {
            ClienteId = cliente.Id, Competencia = competencia,
            Vencimento = r.Vencimento ?? CobrancasService.VencimentoDe(cliente, competencia),
            Valor = Math.Round(valor, 2),
            Descricao = string.IsNullOrWhiteSpace(r.Descricao) ? CobrancasService.Descricao(cliente, cliente.Plano, competencia) : r.Descricao.Trim(),
        };
        db.Faturas.Add(fatura);
        await db.SaveChangesAsync();
        await cobrancas.SincronizarAsync(Relogio.Hoje, cliente.Id); // vira conta no Contas a pagar da loja
        return Ok(new { fatura.Id });
    }

    [HttpPut("faturas/{id:int}")]
    public async Task<IActionResult> Editar(int id, EditarFaturaRequest r)
    {
        var f = await db.Faturas.FirstOrDefaultAsync(x => x.Id == id);
        if (f is null) return NotFound();
        if (f.Status != StatusFatura.Aberta) return BadRequest(new { mensagem = "Só dá para mudar mensalidade em aberto." });
        f.Vencimento = r.Vencimento;
        f.Valor = Math.Round(r.Valor, 2);
        if (!string.IsNullOrWhiteSpace(r.Descricao)) f.Descricao = r.Descricao.Trim();
        await db.SaveChangesAsync();
        await cobrancas.SincronizarAsync(Relogio.Hoje, f.ClienteId); // a conta da loja acompanha
        await cobrancas.LiberarSeEmDiaAsync(f.ClienteId, Relogio.Hoje); // vencimento adiado pode tirar do bloqueio
        return Ok();
    }

    // Recebeu: marca paga. Se o cliente estava bloqueado por atraso e ficou em dia, volta sozinho.
    [HttpPost("faturas/{id:int}/pagar")]
    public async Task<IActionResult> Pagar(int id, PagarFaturaRequest r)
    {
        var f = await db.Faturas.FirstOrDefaultAsync(x => x.Id == id);
        if (f is null) return NotFound();
        if (f.Status != StatusFatura.Aberta) return BadRequest(new { mensagem = "Esta mensalidade não está em aberto." });
        if (!FormasRecebimento.Todas.Contains(r.Forma)) return BadRequest(new { mensagem = "Forma de pagamento inválida." });
        var pagaEm = r.PagaEm ?? Relogio.Hoje;
        if (pagaEm > Relogio.Hoje) return BadRequest(new { mensagem = "A data do pagamento não pode ser no futuro." });
        f.Status = StatusFatura.Paga;
        f.PagaEm = pagaEm;
        f.ValorPago = Math.Round(r.ValorPago ?? f.Valor, 2);
        f.FormaPagamento = r.Forma;
        f.Observacao = string.IsNullOrWhiteSpace(r.Observacao) ? f.Observacao : r.Observacao.Trim();
        await db.SaveChangesAsync();
        var liberado = await cobrancas.LiberarSeEmDiaAsync(f.ClienteId, Relogio.Hoje);
        return Ok(new { liberado });
    }

    [HttpPost("faturas/{id:int}/desfazer-pagamento")]
    public async Task<IActionResult> DesfazerPagamento(int id)
    {
        var f = await db.Faturas.FirstOrDefaultAsync(x => x.Id == id);
        if (f is null) return NotFound();
        if (f.Status != StatusFatura.Paga) return BadRequest(new { mensagem = "Esta mensalidade não está paga." });
        if (f.PagaPelaLoja) return BadRequest(new { mensagem = "Foi paga pela loja (baixa no Contas a pagar dela): o pagamento se desfaz lá." });
        (f.Status, f.PagaEm, f.ValorPago, f.FormaPagamento) = (StatusFatura.Aberta, null, null, null);
        await db.SaveChangesAsync();
        return Ok(); // o bloqueio (se for o caso) vem na próxima rotina, como para qualquer atraso
    }

    [HttpPost("faturas/{id:int}/cancelar")]
    public async Task<IActionResult> Cancelar(int id, CancelarFaturaRequest r)
    {
        var f = await db.Faturas.FirstOrDefaultAsync(x => x.Id == id);
        if (f is null) return NotFound();
        if (f.Status != StatusFatura.Aberta) return BadRequest(new { mensagem = "Só dá para cancelar mensalidade em aberto." });
        f.Status = StatusFatura.Cancelada;
        f.Observacao = string.IsNullOrWhiteSpace(r.Motivo) ? "Cancelada" : r.Motivo.Trim();
        await db.SaveChangesAsync();
        await cobrancas.SincronizarAsync(Relogio.Hoje, f.ClienteId); // a conta em aberto sai da loja
        await cobrancas.LiberarSeEmDiaAsync(f.ClienteId, Relogio.Hoje);
        return Ok();
    }

    // ------------------------------------------------------------------ regras

    [HttpGet("cobranca/configuracao")]
    public async Task<ConfiguracaoCobrancaDto> Configuracao()
    {
        var c = await cobrancas.ConfiguracaoAsync();
        return new(c.DiasGerarAntes, c.BloqueioAutomatico, c.DiasParaBloquear, c.DiasAvisoAntes, c.ChavePix, c.NomeRecebedor, c.ContatoCobranca);
    }

    [HttpPut("cobranca/configuracao")]
    public async Task<ConfiguracaoCobrancaDto> SalvarConfiguracao(ConfiguracaoCobrancaDto r)
    {
        var c = await db.ConfiguracoesPlataforma.FirstOrDefaultAsync(x => x.Id == 1);
        if (c is null) db.ConfiguracoesPlataforma.Add(c = new ConfiguracaoPlataforma());
        static string? Limpo(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        (c.DiasGerarAntes, c.BloqueioAutomatico, c.DiasParaBloquear, c.DiasAvisoAntes) =
            (r.DiasGerarAntes, r.BloqueioAutomatico, r.DiasParaBloquear, r.DiasAvisoAntes);
        (c.ChavePix, c.NomeRecebedor, c.ContatoCobranca) =
            (Limpo(r.ChavePix), Limpo(r.NomeRecebedor), Limpo(Documentos.SoDigitos(r.ContatoCobranca)));
        await db.SaveChangesAsync();
        return await Configuracao();
    }

    private static FaturaResponse Resposta(Fatura f, DateOnly hoje) => new(f.Id, f.ClienteId, f.Cliente?.Nome ?? "?", f.Cliente?.Telefone,
        f.Cliente?.Responsavel, f.Competencia, f.Vencimento, f.Valor, f.Descricao, f.Status, f.PagaEm, f.ValorPago, f.FormaPagamento,
        f.Observacao, CobrancasService.DiasDeAtraso(f, hoje));
}
