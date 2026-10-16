using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Models;
using Marketplace.Api.Services.Conciliacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

// Conciliação bancária: importar o extrato OFX do banco e conferir com o livro. Ver Services/Conciliacao.
[ApiController]
[Route("api/financeiro/conciliacao")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class ConciliacaoController(ConciliacaoService conciliacao) : ControllerBase
{
    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // GET /api/financeiro/conciliacao?contaId=3 → pendentes (com a sugestão), saldo do banco × sistema, o que falta no banco
    [HttpGet]
    public Task<ConciliacaoResponse> Consultar(int? contaId) => conciliacao.ConsultarAsync(contaId);

    // POST /api/financeiro/conciliacao/importar  (multipart: "arquivo" = o .ofx; "contaId" opcional: sem ela, acha pela conta do arquivo)
    [HttpPost("importar")]
    [RequestSizeLimit(LeitorOfx.TamanhoMaximo + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = LeitorOfx.TamanhoMaximo + 64 * 1024)]
    public async Task<ActionResult<ImportacaoExtratoResponse>> Importar(IFormFile? arquivo, [FromForm] int? contaId)
    {
        if (arquivo is null || arquivo.Length == 0) return BadRequest(new { mensagem = "Escolha o arquivo .ofx do extrato." });
        if (arquivo.Length > LeitorOfx.TamanhoMaximo) return BadRequest(new { mensagem = "Arquivo grande demais (máximo 5 MB): exporte um período menor." });
        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria);
        try { return await conciliacao.ImportarAsync(memoria.ToArray(), arquivo.FileName, contaId, UsuarioId); }
        catch (EscolherContaException e) { return BadRequest(new { mensagem = e.Message, escolherConta = true }); }
        catch (ExtratoInvalidoException e) { return BadRequest(new { mensagem = e.Message }); }
    }

    // GET /api/financeiro/conciliacao/linhas/5/candidatos → lançamentos que podem ser esta linha (para escolher na mão)
    [HttpGet("linhas/{id:int}/candidatos")]
    public Task<ActionResult<List<MovimentoResumo>>> Candidatos(int id) => Tentar(async () => await conciliacao.CandidatosAsync(id));

    // POST /api/financeiro/conciliacao/conciliar  { linhaIds, movimentoIds } → as somas precisam bater
    [HttpPost("conciliar")]
    public Task<ActionResult<object>> Conciliar(ConciliarRequest r) =>
        Tentar<object>(async () => new { conciliacaoId = await conciliacao.ConciliarAsync(r.LinhaIds ?? [], r.MovimentoIds ?? [], UsuarioId) });

    // POST /api/financeiro/conciliacao/aceitar-sugestoes  { contaId } → confere todas as sugestões de uma vez
    [HttpPost("aceitar-sugestoes")]
    public Task<ActionResult<object>> AceitarSugestoes(AceitarSugestoesRequest r) =>
        Tentar<object>(async () => new { conferidas = await conciliacao.AceitarSugestoesAsync(r.ContaId, UsuarioId) });

    // POST /api/financeiro/conciliacao/linhas/5/lancar  { categoriaId, descricao } → lança no sistema o que só o banco tinha
    [HttpPost("linhas/{id:int}/lancar")]
    public Task<ActionResult<MovimentoResumo>> Lancar(int id, LancarLinhaRequest r) =>
        Tentar(async () => await conciliacao.LancarAsync(id, r.CategoriaId, r.Descricao, UsuarioId));

    // POST /api/financeiro/conciliacao/linhas/5/ignorar?valor=true|false
    [HttpPost("linhas/{id:int}/ignorar")]
    public Task<ActionResult<object>> Ignorar(int id, bool valor = true) =>
        Tentar<object>(async () => { await conciliacao.IgnorarAsync(id, valor); return new { ok = true }; });

    // POST /api/financeiro/conciliacao/{guid}/desfazer → volta a pendente (nada é apagado)
    [HttpPost("{conciliacaoId:guid}/desfazer")]
    public Task<ActionResult<object>> Desfazer(Guid conciliacaoId) =>
        Tentar<object>(async () => { await conciliacao.DesfazerAsync(conciliacaoId); return new { ok = true }; });

    private async Task<ActionResult<T>> Tentar<T>(Func<Task<T>> acao)
    {
        try { return await acao(); }
        catch (ConciliacaoException e) { return BadRequest(new { mensagem = e.Message }); }
    }
}
