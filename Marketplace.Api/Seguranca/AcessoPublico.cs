namespace Marketplace.Api.Seguranca;

// Pedido que veio da INTERNET (pelo Cloudflare Tunnel, ex.: demo.wnlabs.com.br) ou da rede interna
// (Tailscale / a própria VM)? O cloudflared entrega tudo em 127.0.0.1, então o IP não serve para
// saber; mas a Cloudflare sempre coloca o cabeçalho CF-Connecting-IP (com o IP real do visitante),
// e ninguém de fora consegue chegar na porta 5100 sem passar por ela (firewall).
public static class AcessoPublico
{
    public const string CabecalhoIp = "CF-Connecting-IP";

    public static bool VeioDaInternet(this HttpContext ctx) => ctx.Request.Headers.ContainsKey(CabecalhoIp);

    // IP de verdade de quem está acessando (para o limite de tentativas por pessoa).
    public static string IpReal(this HttpContext ctx) =>
        ctx.Request.Headers[CabecalhoIp].FirstOrDefault() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "?";

    // Pela internet, algumas portas ficam FECHADAS:
    //  • /api/integracao → só o n8n usa, e ele está dentro da VM;
    //  • /openapi        → documentação da API (só em desenvolvimento, mas por garantia).
    private static readonly string[] SoInterno = ["/api/integracao", "/openapi"];

    public static async Task Middleware(HttpContext ctx, Func<Task> proximo)
    {
        if (ctx.VeioDaInternet())
        {
            if (SoInterno.Any(p => ctx.Request.Path.StartsWithSegments(p)))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Cabeçalhos de segurança: não deixa outro site "embutir" o sistema (clickjacking), nem o
            // navegador "adivinhar" tipo de arquivo, nem vazar o endereço completo para outros sites.
            ctx.Response.Headers.XFrameOptions = "DENY";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        }
        await proximo();
    }
}
