using System.Security.Claims;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Seguranca;

// Ao logar, o Identity monta o "crachá" (ClaimsPrincipal) que vai dentro do cookie.
// Aqui acrescentamos uma marca quando a senha é PROVISÓRIA. Ao trocar a senha, o cookie é renovado sem a marca.
public class MarcaSenhaProvisoria(UserManager<Usuario> usuarios, RoleManager<IdentityRole> perfis, IOptions<IdentityOptions> opcoes)
    : UserClaimsPrincipalFactory<Usuario, IdentityRole>(usuarios, perfis, opcoes)
{
    public const string Claim = "senha_provisoria";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Usuario usuario)
    {
        var identidade = await base.GenerateClaimsAsync(usuario);
        if (usuario.TrocarSenha)
            identidade.AddClaim(new Claim(Claim, "1"));

        // De qual empresa é a pessoa (multi-tenant) e se é visitante da demonstração (só olha).
        identidade.AddClaim(new Claim(ContextoEmpresa.Claim, usuario.EmpresaId.ToString()));
        if (usuario.SomenteLeitura)
            identidade.AddClaim(new Claim(SomenteLeitura.Claim, "1"));
        return identidade;
    }

    // "Porteiro": com senha provisória, só /api/auth/* (login, logout, eu, trocar-senha) funciona.
    // Assim a regra vale no SERVIDOR — não dá para pular a troca chamando a API direto.
    public static async Task Middleware(HttpContext ctx, Func<Task> proximo)
    {
        var caminho = ctx.Request.Path;
        if (ctx.User.HasClaim(c => c.Type == Claim) && caminho.StartsWithSegments("/api") && !caminho.StartsWithSegments("/api/auth"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { mensagem = "Troque a senha provisória antes de continuar." });
            return;
        }
        await proximo();
    }
}
