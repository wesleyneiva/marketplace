using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Marketplace.Api.Models;
using Marketplace.Api.Services.Pix;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

// Pix automático (Mercado Pago): o caixa gera a cobrança, acompanha até ser paga; o Mercado Pago avisa no webhook.
[ApiController]
[Route("api/pix")]
[Authorize]
public class PixController(PixAutomaticoService pix, ILogger<PixController> log) : ControllerBase
{
    public record CriarCobrancaRequest([Range(0.01, 999999.99)] decimal Valor, int? SessaoCaixaId);
    public record CobrancaPixResponse(int Id, decimal Valor, StatusCobrancaPix Status, string CopiaECola, string Svg,
        DateTimeOffset ExpiraEm, DateTimeOffset? PagaEm);
    public record LigarMercadoPagoRequest([Required, StringLength(300)] string Token, bool Teste);

    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // POST /api/pix/cobrancas  { valor, sessaoCaixaId } → QR único desta venda no Mercado Pago
    [HttpPost("cobrancas")]
    public Task<ActionResult<CobrancaPixResponse>> Criar(CriarCobrancaRequest r) =>
        Tentar(async () => Resposta(await pix.CriarAsync(r.Valor, r.SessaoCaixaId, UsuarioId), comQr: true));

    // GET /api/pix/cobrancas/5 → situação (pergunta ao Mercado Pago enquanto estiver pendente)
    [HttpGet("cobrancas/{id:int}")]
    public Task<ActionResult<CobrancaPixResponse>> Situacao(int id) => Tentar(async () => Resposta(await pix.SituacaoAsync(id), comQr: false));

    // POST /api/pix/cobrancas/5/cancelar → o cliente desistiu ou vai pagar de outro jeito
    [HttpPost("cobrancas/{id:int}/cancelar")]
    public Task<ActionResult<CobrancaPixResponse>> Cancelar(int id) => Tentar(async () => Resposta(await pix.CancelarAsync(id), comQr: false));

    // GET /api/pix/cobrancas/soltas?sessaoCaixaId=3 → Pix pagos que ainda não entraram em nenhuma venda
    [HttpGet("cobrancas/soltas")]
    public async Task<List<CobrancaPixResponse>> Soltas(int sessaoCaixaId) =>
        (await pix.SoltasAsync(sessaoCaixaId)).Select(c => Resposta(c, comQr: false)).ToList();

    // PUT /api/pix/mercado-pago  { token, teste } → liga o Pix automático (confere o token no Mercado Pago antes de guardar)
    [HttpPut("mercado-pago")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<IActionResult> Ligar(LigarMercadoPagoRequest r)
    {
        try
        {
            var e = await pix.LigarMercadoPagoAsync(r.Token, r.Teste);
            return Ok(new { conta = e.MercadoPagoConta, final = e.MercadoPagoTokenFinal, teste = e.MercadoPagoTeste });
        }
        catch (Exception e) when (e is PixException or MercadoPagoException) { return BadRequest(new { mensagem = e.Message }); }
    }

    // DELETE /api/pix/mercado-pago → volta ao Pix manual (o token é apagado)
    [HttpDelete("mercado-pago")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<IActionResult> Desligar()
    {
        await pix.DesligarAsync();
        return NoContent();
    }

    // POST /api/pix/mercadopago/aviso/{segredo da loja}?data.id=123&type=payment  (o Mercado Pago chama; sem login)
    // O aviso NÃO é prova de pagamento: só faz o Marcus perguntar ao Mercado Pago, com o token da loja, sobre o pagamento.
    [HttpPost("mercadopago/aviso/{segredo}")]
    [AllowAnonymous]
    public async Task<IActionResult> Aviso(string segredo)
    {
        if (segredo.Length is < 16 or > 40) return NotFound();
        string? id = Request.Query["data.id"].FirstOrDefault() ?? Request.Query["id"].FirstOrDefault();
        if (id is null && Request.ContentLength is > 0 and < 64 * 1024)
        {
            try
            {
                using var corpo = await JsonDocument.ParseAsync(Request.Body);
                if (corpo.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("id", out var i)) id = i.ToString();
            }
            catch (JsonException) { /* corpo estranho: sem id */ }
        }
        if (id is not null && (id.Length > 40 || !id.All(char.IsAsciiLetterOrDigit))) return BadRequest();
        try { return await pix.AvisoAsync(segredo, id) ? Ok() : NotFound(); }
        catch (Exception e) when (e is PixException or MercadoPagoException)
        {
            log.LogWarning("Aviso do Mercado Pago: {Erro}", e.Message);
            return Ok(); // o caixa também consulta sozinho; não precisa o Mercado Pago repetir
        }
    }

    private static CobrancaPixResponse Resposta(CobrancaPix c, bool comQr) =>
        new(c.Id, c.Valor, c.Status, comQr ? c.CopiaECola : "", comQr ? PixQrCode.Svg(c.CopiaECola) : "", c.ExpiraEm, c.PagaEm);

    private static async Task<ActionResult<T>> Tentar<T>(Func<Task<T>> acao)
    {
        try { return await acao(); }
        catch (Exception e) when (e is PixException or MercadoPagoException) { return new BadRequestObjectResult(new { mensagem = e.Message }); }
    }
}
