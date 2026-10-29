using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Marketplace.Api.Services.Simulador;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Disponivel = esta empresa tem vendas automáticas (senão o painel nem mostra o cartão do simulador).
// CaixaComOutro = uma pessoa está com o caixa do robô aberto (o robô espera ela fechar para vender).
public record SimuladorStatus(
    bool Disponivel, int NumeroCaixa, bool Ativo, double Intensidade, bool CaixaAberto, DateTimeOffset? UltimaRodada, string? UltimoErro,
    decimal? Temperatura, string? Tempo, bool ClimaReal, string? Cidade, int VendasHoje, decimal FaturamentoHoje,
    string? CaixaComOutro, DateTimeOffset? CaixaComOutroDesde);

[ApiController]
[Route("api/simulador")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class SimuladorController(SimuladorEstado estado, SimuladorClientes simulador, ClimaService clima, LocaisDasLojas locais, AppDbContext db, GeradorHistorico gerador)
    : ControllerBase
{
    // GET /api/simulador → situação do bot NESTA empresa + vendas automáticas de hoje
    [HttpGet]
    public async Task<SimuladorStatus> Status()
    {
        var empresaId = db.EmpresaAtual;
        var modo = await ModoAsync();
        var loja = estado.DaEmpresa(empresaId);
        var inicio = Relogio.InicioDoDiaUtc(Relogio.Hoje);
        var deHoje = db.Vendas.AsNoTracking().Where(v =>
            v.Origem == OrigemVenda.Simulador && v.Status == StatusVenda.Concluida && v.DataHora >= inicio);
        var email = SimuladorEstado.EmailDoRobo(empresaId);
        var aberto = await db.SessoesCaixa.AnyAsync(s => s.Usuario!.Email == email && s.Status == StatusSessao.Aberta);
        var local = locais.Local(empresaId);
        var tempo = local is null ? null : clima.Ultimo(local) ?? await clima.ObterAsync(local, HttpContext.RequestAborted);

        var limite = await db.Empresas.Where(e => e.Id == empresaId).Select(e => e.LimiteCaixas).FirstAsync();
        var numero = SimuladorEstado.CaixaDoRobo(modo == ModosVendasAutomaticas.Completo, limite);
        var outro = aberto ? null : await db.SessoesCaixa.AsNoTracking()
            .Where(s => s.NumeroCaixa == numero && s.Status == StatusSessao.Aberta && s.Usuario!.Email != email)
            .Select(s => new { s.Usuario!.NomeCompleto, s.AbertaEm }).FirstOrDefaultAsync();
        return new SimuladorStatus(modo != ModosVendasAutomaticas.Desligado,
            numero,
            estado.Ativo && modo != ModosVendasAutomaticas.Desligado && !loja.Pausado,
            estado.Intensidade, aberto, loja.UltimaRodada, loja.UltimoErro,
            tempo?.Temperatura, tempo?.Descricao, tempo?.Real ?? false, local?.Nome,
            await deHoje.CountAsync(), await deHoje.SumAsync(v => (decimal?)v.Total) ?? 0,
            outro?.NomeCompleto, outro?.AbertaEm);
    }

    // POST /api/simulador/pausar  e  /retomar  (só Administrador; só nesta empresa, até o sistema reiniciar)
    [HttpPost("pausar")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<SimuladorStatus> Pausar()
    {
        estado.DaEmpresa(db.EmpresaAtual).Pausado = true;
        await simulador.FecharCaixaDoRoboAsync(db.EmpresaAtual); // libera o caixa para uma pessoa
        return await Status();
    }

    [HttpPost("retomar")]
    [Authorize(Roles = Perfis.Administrador)]
    public Task<SimuladorStatus> Retomar() { estado.DaEmpresa(db.EmpresaAtual).Pausado = false; return Status(); }

    // POST /api/simulador/clientes?quantidade=5 → atende N clientes AGORA (testes e demonstração)
    [HttpPost("clientes")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<IActionResult> AtenderAgora(int quantidade = 5)
    {
        var modo = await ModoAsync();
        if (modo == ModosVendasAutomaticas.Desligado)
            return BadRequest(new { mensagem = "Esta empresa não tem vendas automáticas." });
        var limite = await db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.LimiteCaixas).FirstAsync();
        int vendidos;
        try
        {
            vendidos = await simulador.AtenderAgoraAsync(db.EmpresaAtual, modo == ModosVendasAutomaticas.Completo, limite,
                Math.Clamp(quantidade, 1, 50), HttpContext.RequestAborted);
        }
        catch (CaixaException e)
        {
            return BadRequest(new { mensagem = e.Message }); // ex.: o caixa está com uma pessoa
        }
        return Ok(new { clientes = quantidade, vendasFeitas = vendidos });
    }

    private Task<string> ModoAsync() =>
        db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.VendasAutomaticas).FirstAsync();

    // POST /api/simulador/historico?dias=60&substituir=false → gera o passado (vendas sem mexer no estoque)
    [HttpPost("historico")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<IActionResult> GerarHistorico(int dias = 60, bool substituir = false)
    {
        if (db.EmpresaAtual != estado.EmpresaId)
            return BadRequest(new { mensagem = "O histórico gerado existe só na empresa de demonstração." });
        try
        {
            return Ok(await gerador.GerarAsync(dias, substituir, HttpContext.RequestAborted));
        }
        catch (InvalidOperationException e)
        {
            return Conflict(new { mensagem = e.Message });
        }
    }

    // DELETE /api/simulador/historico → apaga só o histórico gerado
    [HttpDelete("historico")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<IActionResult> ApagarHistorico() => db.EmpresaAtual != estado.EmpresaId
        ? BadRequest(new { mensagem = "O histórico gerado existe só na empresa de demonstração." })
        : Ok(new { vendasApagadas = await gerador.ApagarAsync(HttpContext.RequestAborted) });
}
