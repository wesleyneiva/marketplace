using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Importação de clientes por planilha (a lista do sistema antigo da loja). Administrador e Gerente, como o cadastro.
[ApiController]
[Route("api/clientes/importacao")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class ImportacaoClientesController(AppDbContext db, ContextoEmpresa contexto, ImportacaoClientesService importacao) : ControllerBase
{
    private const int TamanhoMaximo = 5 * 1024 * 1024;
    private const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // GET /api/clientes/importacao/modelo → planilha .xlsx para preencher (com raquete/corda/tensão no ramo com oficina).
    [HttpGet("modelo")]
    public async Task<IActionResult> Modelo()
    {
        var ramo = await db.Empresas.AsNoTracking().Where(e => e.Id == contexto.EmpresaId).Select(e => e.Ramo).FirstAsync();
        return File(importacao.GerarModelo(comOficina: ramo == Ramos.Esporte), TipoXlsx, "modelo-clientes.xlsx");
    }

    // POST /api/clientes/importacao/previa  (multipart, campo "arquivo") → o que aconteceria com cada linha. NÃO grava nada.
    [HttpPost("previa")]
    [RequestSizeLimit(TamanhoMaximo)]
    [RequestFormLimits(MultipartBodyLengthLimit = TamanhoMaximo)]
    public async Task<ActionResult<PreviaImportacaoClientesResponse>> Previa(IFormFile? arquivo)
    {
        if (arquivo is null || arquivo.Length == 0) return BadRequest(new { mensagem = "Escolha a planilha (.xlsx ou .csv)." });
        await using var conteudo = arquivo.OpenReadStream();
        return await importacao.GerarPreviaAsync(conteudo, Path.GetFileName(arquivo.FileName));
    }

    // POST /api/clientes/importacao  (multipart, campo "arquivo") → GRAVA as linhas sem erro (confere a planilha de novo).
    [HttpPost]
    [RequestSizeLimit(TamanhoMaximo)]
    [RequestFormLimits(MultipartBodyLengthLimit = TamanhoMaximo)]
    public async Task<ActionResult<ResultadoImportacaoClientesResponse>> Importar(IFormFile? arquivo)
    {
        if (arquivo is null || arquivo.Length == 0) return BadRequest(new { mensagem = "Escolha a planilha (.xlsx ou .csv)." });
        try
        {
            await using var conteudo = arquivo.OpenReadStream();
            return await importacao.ImportarAsync(conteudo, Path.GetFileName(arquivo.FileName), User.FindFirstValue(ClaimTypes.NameIdentifier));
        }
        catch (ImportacaoException e) { return BadRequest(new { mensagem = e.Message }); }
    }
}
