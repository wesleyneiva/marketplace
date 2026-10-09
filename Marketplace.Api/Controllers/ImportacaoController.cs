using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Importação de produtos por planilha. Só quem pode cadastrar produtos (Administrador e Gerente).
[ApiController]
[Route("api/produtos/importacao")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class ImportacaoController(AppDbContext db, ImportacaoProdutosService importacao) : ControllerBase
{
    private const int TamanhoMaximo = 5 * 1024 * 1024; // 5 MB: dá folga para ~50 mil linhas
    private const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // GET /api/produtos/importacao/modelo → planilha .xlsx para preencher, com as categorias DA EMPRESA no menu.
    [HttpGet("modelo")]
    public async Task<IActionResult> Modelo()
    {
        var categorias = await db.Categorias.AsNoTracking().Where(c => c.Ativa)
            .OrderBy(c => c.Nome).Select(c => c.Nome).ToListAsync();
        return File(importacao.GerarModelo(categorias), TipoXlsx, "modelo-produtos.xlsx");
    }

    // POST /api/produtos/importacao/previa  (multipart, campo "arquivo") → o que aconteceria com cada linha.
    // NÃO grava nada: é só para o cliente conferir e corrigir a planilha antes de confirmar.
    [HttpPost("previa")]
    [RequestSizeLimit(TamanhoMaximo)]
    [RequestFormLimits(MultipartBodyLengthLimit = TamanhoMaximo)]
    public async Task<ActionResult<PreviaImportacaoResponse>> Previa(IFormFile? arquivo)
    {
        if (arquivo is null || arquivo.Length == 0)
            return BadRequest(new { mensagem = "Escolha a planilha (.xlsx ou .csv)." });
        await using var conteudo = arquivo.OpenReadStream();
        return await importacao.GerarPreviaAsync(conteudo, Path.GetFileName(arquivo.FileName));
    }
}
