using System.Security.Claims;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marketplace.Api.Seguranca;

// "Dono da plataforma" = quem administra o SaaS (clientes, planos, cobranças): Administrador da empresa marcada
// como Plataforma no banco (criada pelo comando criar-dono). Enquanto ela não existir, vale Plataforma:EmpresaId
// (padrão 1, a demonstração) — o jeito antigo. Nenhum cliente enxerga isso.
public static class Plataforma
{
    private static int? _doBanco;

    // Lida ao ligar a API (Program.cs) e atualizada pelo criar-dono.
    public static void Definir(int? empresaId) => _doBanco = empresaId;

    public static int EmpresaDona(IConfiguration config) => _doBanco ?? config.GetValue("Plataforma:EmpresaId", 1);

    // true quando a área do dono já é separada (existe a empresa da plataforma).
    public static bool Separada => _doBanco is not null;

    public static bool EhDono(ClaimsPrincipal usuario, IConfiguration config) =>
        usuario.Identity?.IsAuthenticated == true
        && usuario.IsInRole(Perfis.Administrador)
        && !usuario.HasClaim(c => c.Type == SomenteLeitura.Claim)
        && usuario.FindFirstValue(ContextoEmpresa.Claim) == EmpresaDona(config).ToString();
}

// Porta das telas da plataforma: só o dono, e só pela rede interna (Tailscale) — pela internet nem existe (404).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class DonoDaPlataformaAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext contexto)
    {
        var http = contexto.HttpContext;
        if (http.VeioDaInternet())
            contexto.Result = new NotFoundResult();
        else if (http.User.Identity?.IsAuthenticated != true)
            contexto.Result = new UnauthorizedResult();
        else if (!Plataforma.EhDono(http.User, http.RequestServices.GetRequiredService<IConfiguration>()))
            contexto.Result = new ForbidResult();
    }
}
