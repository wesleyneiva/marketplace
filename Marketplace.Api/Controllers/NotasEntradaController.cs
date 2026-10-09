using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Marketplace.Api.Contracts;
using Marketplace.Api.Models;
using Marketplace.Api.Services.NotaFiscal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

// Entrada de mercadoria pelo XML da NF-e. Só Administrador e Gerente (quem cuida das compras).
[ApiController]
[Route("api/compras/notas")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class NotasEntradaController(EntradaNotaService notas) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // GET /api/compras/notas → últimas notas lançadas
    [HttpGet]
    public Task<List<NotaEntradaResponse>> Listar() => notas.ListarAsync(50);

    // GET /api/compras/notas/5/xml → baixa o XML guardado
    [HttpGet("{id:int}/xml")]
    public async Task<IActionResult> Xml(int id) =>
        await notas.XmlAsync(id) is { } x ? File(Encoding.UTF8.GetBytes(x.Xml), "application/xml", x.Nome) : NotFound();

    // POST /api/compras/notas/conferencia  (multipart, campo "arquivo" = o XML) → o que o sistema entendeu. NÃO grava.
    [HttpPost("conferencia")]
    [RequestSizeLimit(EntradaNotaService.TamanhoMaximo)]
    [RequestFormLimits(MultipartBodyLengthLimit = EntradaNotaService.TamanhoMaximo)]
    public async Task<ActionResult<ConferenciaNotaResponse>> Conferir(IFormFile? arquivo)
    {
        if (await LerAsync(arquivo) is not { } xml)
            return BadRequest(new { mensagem = "Escolha o arquivo XML da nota." });
        try { return await notas.ConferirAsync(xml); }
        catch (NotaEntradaException e) { return BadRequest(new { mensagem = e.Message }); }
    }

    // POST /api/compras/notas  (multipart: "arquivo" = o XML de novo + "dados" = JSON com as decisões) → GRAVA.
    // O XML vai de novo para o servidor reler e conferir (não confia no que ficou na tela).
    [HttpPost]
    [RequestSizeLimit(EntradaNotaService.TamanhoMaximo + 512 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = EntradaNotaService.TamanhoMaximo + 512 * 1024)]
    public async Task<ActionResult<RegistroNotaResponse>> Registrar(IFormFile? arquivo, [FromForm] string? dados)
    {
        if (await LerAsync(arquivo) is not { } xml)
            return BadRequest(new { mensagem = "Escolha o arquivo XML da nota." });
        RegistrarNotaRequest? pedido;
        try { pedido = JsonSerializer.Deserialize<RegistrarNotaRequest>(dados ?? "", Json); }
        catch (JsonException) { pedido = null; }
        if (pedido?.Itens is null)
            return BadRequest(new { mensagem = "Faltam as decisões dos itens." });

        try { return await notas.RegistrarAsync(xml, pedido, User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
        catch (NotaEntradaException e) { return BadRequest(new { mensagem = e.Message }); }
    }

    private static async Task<byte[]?> LerAsync(IFormFile? arquivo)
    {
        if (arquivo is null || arquivo.Length == 0 || arquivo.Length > EntradaNotaService.TamanhoMaximo) return null;
        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria);
        return memoria.ToArray();
    }
}
