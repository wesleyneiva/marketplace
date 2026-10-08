using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Marketplace.Api.Services.Simulador;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

public record SimuladorStatus(
    bool Ativo, double Intensidade, bool CaixaAberto, DateTimeOffset? UltimaRodada, string? UltimoErro,
    decimal? Temperatura, string? Tempo, bool ClimaReal, int VendasHoje, decimal FaturamentoHoje);

[ApiController]
[Route("api/simulador")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class SimuladorController(SimuladorEstado estado, SimuladorClientes simulador, ClimaService clima, AppDbContext db)
    : ControllerBase
{
    // GET /api/simulador → situação + vendas simuladas de hoje
    [HttpGet]
    public async Task<SimuladorStatus> Status()
    {
        var inicio = Relogio.InicioDoDiaUtc(Relogio.HojeBrasilia);
        var deHoje = db.Vendas.AsNoTracking().Where(v =>
            v.Usuario!.Email == SimuladorEstado.Email && v.Status == StatusVenda.Concluida && v.DataHora >= inicio);
        var aberto = await db.SessoesCaixa.AnyAsync(s => s.Usuario!.Email == SimuladorEstado.Email && s.Status == StatusSessao.Aberta);
        var tempo = clima.Ultimo ?? await clima.ObterAsync(HttpContext.RequestAborted);

        return new SimuladorStatus(estado.Ativo, estado.Intensidade, aberto, estado.UltimaRodada, estado.UltimoErro,
            tempo.Temperatura, tempo.Descricao, tempo.Real,
            await deHoje.CountAsync(), await deHoje.SumAsync(v => (decimal?)v.Total) ?? 0);
    }

    // POST /api/simulador/pausar  e  /retomar  (só Administrador)
    [HttpPost("pausar")]
    [Authorize(Roles = Perfis.Administrador)]
    public Task<SimuladorStatus> Pausar() { estado.Ativo = false; return Status(); }

    [HttpPost("retomar")]
    [Authorize(Roles = Perfis.Administrador)]
    public Task<SimuladorStatus> Retomar() { estado.Ativo = true; return Status(); }

    // POST /api/simulador/clientes?quantidade=5 → atende N clientes AGORA (testes e demonstração)
    [HttpPost("clientes")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<object> AtenderAgora(int quantidade = 5)
    {
        var vendidos = await simulador.AtenderAgoraAsync(Math.Clamp(quantidade, 1, 50), HttpContext.RequestAborted);
        return new { clientes = quantidade, vendasFeitas = vendidos };
    }
}
