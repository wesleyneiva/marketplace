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
public class EmpresaController(AppDbContext db, ContextoEmpresa contexto, LocaisDasLojas locais, ColetorClima coletor, IHttpClientFactory http) : ControllerBase
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

    // GET /api/empresa/configuracao/cidades?busca=manaus → cidades do Brasil (com o fuso de cada uma)
    [HttpGet("cidades")]
    public async Task<ActionResult<List<CidadeResponse>>> Cidades(string? busca, CancellationToken ct)
    {
        var termo = busca?.Trim() ?? "";
        if (termo.Length < 2) return new List<CidadeResponse>();
        try
        {
            return await Geografia.BuscarCidadesAsync(http.CreateClient(), termo, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { mensagem = "A busca de cidades não respondeu. Tente de novo." });
        }
    }

    // PUT /api/empresa/configuracao/cidade → onde fica a loja (clima + fuso horário)
    [HttpPut("cidade")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<ActionResult<ConfiguracaoEmpresaResponse>> SalvarCidade(CidadeRequest r)
    {
        if (!Geografia.FusoValido(r.Fuso)) return BadRequest(new { mensagem = "Fuso horário desconhecido." });
        var empresa = await CarregarAsync();
        empresa.Cidade = r.Nome.Trim();
        empresa.Uf = r.Uf.Trim().ToUpperInvariant();
        empresa.Latitude = Math.Round(r.Latitude, 5);
        empresa.Longitude = Math.Round(r.Longitude, 5);
        empresa.Fuso = r.Fuso;
        await db.SaveChangesAsync();
        locais.Esquecer(empresa.Id);
        coletor.Acordar(); // busca o clima da cidade nova (e os últimos 60 dias) agora
        Relogio.UsarFuso(empresa.Fuso); // a resposta já sai com a hora da cidade nova
        return Resposta(empresa);
    }

    private Task<Empresa> CarregarAsync() => db.Empresas.FirstAsync(e => e.Id == contexto.EmpresaId);

    // Exemplo para a tela mostrar como fica a etiqueta: produto 42, R$ 15,99 (ou 1,235 kg).
    private static ConfiguracaoEmpresaResponse Resposta(Empresa e) => new(e.BalancaDigitosCodigo, e.BalancaEtiqueta,
        EtiquetaBalanca.Montar(42, e.BalancaEtiqueta == EtiquetaBalancaTipos.Peso ? 1235 : 1599, e.BalancaDigitosCodigo),
        e.Cidade, e.Uf, e.Fuso, Relogio.Agora.ToString("dd/MM/yyyy HH:mm"));
}
