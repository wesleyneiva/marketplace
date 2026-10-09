using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Caixa do usuário logado: abrir, sangria/suprimento, ver o resumo e fechar.
[ApiController]
[Route("api/caixa")]
[Authorize]
public class CaixaController(CaixaService caixa, AppDbContext db) : ControllerBase
{
    private string UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET /api/caixa/atual → resumo do MEU caixa aberto (204 se não tenho caixa aberto).
    [HttpGet("atual")]
    public async Task<ActionResult<ResumoCaixaResponse>> Atual()
    {
        var sessao = await caixa.ObterSessaoAbertaAsync(UsuarioId);
        return sessao is null ? NoContent() : await caixa.ResumirAsync(sessao.Id);
    }

    // GET /api/caixa/caixas → os caixas da empresa e quem está em cada um (para escolher na abertura)
    [HttpGet("caixas")]
    public Task<List<CaixaDisponivel>> Caixas() => caixa.CaixasAsync();

    [HttpPost("abrir")]
    public Task<ActionResult<ResumoCaixaResponse>> Abrir(AbrirCaixaRequest r) =>
        Executar(async () => await caixa.ResumirAsync((await caixa.AbrirAsync(UsuarioId, r.NumeroCaixa, r.ValorAbertura)).Id));

    [HttpPost("sangria")]
    public Task<ActionResult<ResumoCaixaResponse>> Sangria(MovimentoCaixaRequest r) =>
        Executar(async () =>
        {
            await caixa.MovimentarAsync(UsuarioId, TipoMovimentoCaixa.Sangria, r.Valor, r.Motivo);
            return await ResumoAtualAsync();
        });

    [HttpPost("suprimento")]
    public Task<ActionResult<ResumoCaixaResponse>> Suprimento(MovimentoCaixaRequest r) =>
        Executar(async () =>
        {
            await caixa.MovimentarAsync(UsuarioId, TipoMovimentoCaixa.Suprimento, r.Valor, r.Motivo);
            return await ResumoAtualAsync();
        });

    [HttpPost("fechar")]
    public Task<ActionResult<ResumoCaixaResponse>> Fechar(FecharCaixaRequest r) =>
        Executar(() => caixa.FecharAsync(UsuarioId, r.ValorContado, r.Observacao));

    // GET /api/caixa/sessoes → últimas sessões (gerência confere os fechamentos e as diferenças).
    [HttpGet("sessoes")]
    [Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
    public async Task<List<ResumoCaixaResponse>> Sessoes(int quantidade = 20)
    {
        var ids = await db.SessoesCaixa.AsNoTracking()
            .OrderByDescending(s => s.AbertaEm).Take(Math.Clamp(quantidade, 1, 100))
            .Select(s => s.Id).ToListAsync();

        var resumos = new List<ResumoCaixaResponse>();
        foreach (var id in ids)
            resumos.Add(await caixa.ResumirAsync(id));
        return resumos;
    }

    private async Task<ResumoCaixaResponse> ResumoAtualAsync() =>
        await caixa.ResumirAsync((await caixa.ObterSessaoAbertaAsync(UsuarioId))!.Id);

    private async Task<ActionResult<ResumoCaixaResponse>> Executar(Func<Task<ResumoCaixaResponse>> operacao)
    {
        try
        {
            return await operacao();
        }
        catch (CaixaException e)
        {
            return BadRequest(new { mensagem = e.Message });
        }
        catch (DbUpdateException)
        {
            // O índice único do banco barrou duas aberturas do mesmo caixa no mesmo instante.
            return Conflict(new { mensagem = "Este caixa acabou de ser aberto por outra pessoa." });
        }
    }
}
