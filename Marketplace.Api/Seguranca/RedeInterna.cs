using System.Security.Claims;

namespace Marketplace.Api.Seguranca;

// Pela REDE INTERNA (Tailscale: http://oracle-a1:5100) só entra o DONO DA PLATAFORMA. Os mercados entram só pelo
// endereço dos clientes (https://app.wnlabs.com.br). Liga com Publico__RedeInternaSoDono=true (produção); no
// desenvolvimento (localhost) fica desligado para os testes de mercado continuarem funcionando.
public static class RedeInterna
{
    public const string MensagemMercado = "Por este endereço só entra o dono da plataforma. Os mercados entram por https://app.wnlabs.com.br";

    public static bool SoDono(this HttpContext ctx) =>
        !ctx.VeioDaInternet() && ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Publico:RedeInternaSoDono", false);

    public static bool EhDaPlataforma(int empresaId, IConfiguration config) => empresaId == Plataforma.EmpresaDona(config);

    // Porteiro: um cookie de MERCADO usado pela rede interna (alguém que entrou antes da regra) não vale: 401 e volta ao login.
    public static async Task Middleware(HttpContext ctx, Func<Task> proximo)
    {
        var caminho = ctx.Request.Path;
        if (ctx.User.Identity?.IsAuthenticated == true && caminho.StartsWithSegments("/api")
            && !caminho.StartsWithSegments("/api/auth/login") && !caminho.StartsWithSegments("/api/auth/logout")
            && !caminho.StartsWithSegments("/api/auth/acesso") && ctx.SoDono()
            && int.TryParse(ctx.User.FindFirstValue(ContextoEmpresa.Claim), out var empresa)
            && !EhDaPlataforma(empresa, ctx.RequestServices.GetRequiredService<IConfiguration>()))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { mensagem = MensagemMercado });
            return;
        }
        await proximo();
    }
}
