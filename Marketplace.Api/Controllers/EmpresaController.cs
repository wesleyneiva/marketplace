using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Configurações da PRÓPRIA empresa (de quem está logado). Ler: todos; mudar: só o Administrador dela.
[ApiController]
[Route("api/empresa/configuracao")]
[Authorize]
public class EmpresaController(AppDbContext db, ContextoEmpresa contexto) : ControllerBase
{
    [HttpGet]
    public async Task<ConfiguracaoEmpresaResponse> Obter() => Resposta(await CarregarAsync());

    [HttpPut]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<ActionResult<ConfiguracaoEmpresaResponse>> Salvar(ConfiguracaoEmpresaRequest r)
    {
        if (r.BalancaEtiqueta is not (EtiquetaBalancaTipos.Preco or EtiquetaBalancaTipos.Peso))
            return BadRequest(new { mensagem = "Etiqueta da balança: Preco ou Peso." });
        var empresa = await CarregarAsync();
        empresa.BalancaDigitosCodigo = r.BalancaDigitosCodigo;
        empresa.BalancaEtiqueta = r.BalancaEtiqueta;
        await db.SaveChangesAsync();
        return Resposta(empresa);
    }

    private Task<Empresa> CarregarAsync() => db.Empresas.FirstAsync(e => e.Id == contexto.EmpresaId);

    // Exemplo para a tela mostrar como fica a etiqueta: produto 42, R$ 15,99 (ou 1,235 kg).
    private static ConfiguracaoEmpresaResponse Resposta(Empresa e) => new(e.BalancaDigitosCodigo, e.BalancaEtiqueta,
        EtiquetaBalanca.Montar(42, e.BalancaEtiqueta == EtiquetaBalancaTipos.Peso ? 1235 : 1599, e.BalancaDigitosCodigo));
}
