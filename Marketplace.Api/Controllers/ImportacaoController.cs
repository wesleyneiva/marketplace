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
    private const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // GET /api/produtos/importacao/modelo → planilha .xlsx para preencher, com as categorias DA EMPRESA no menu.
    [HttpGet("modelo")]
    public async Task<IActionResult> Modelo()
    {
        var categorias = await db.Categorias.AsNoTracking().Where(c => c.Ativa)
            .OrderBy(c => c.Nome).Select(c => c.Nome).ToListAsync();
        return File(importacao.GerarModelo(categorias), TipoXlsx, "modelo-produtos.xlsx");
    }
}
