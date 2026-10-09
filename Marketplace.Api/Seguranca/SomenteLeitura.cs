namespace Marketplace.Api.Seguranca;

// Visitante da demonstração: pode OLHAR tudo (GET), mas qualquer gravação na API é barrada aqui,
// no servidor — esconder botões na tela não basta (dá para chamar a API direto).
public static class SomenteLeitura
{
    public const string Claim = "somente_leitura";

    public static async Task Middleware(HttpContext ctx, Func<Task> proximo)
    {
        var caminho = ctx.Request.Path;
        var gravacao = !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method);
        if (gravacao && ctx.User.HasClaim(c => c.Type == Claim)
            && caminho.StartsWithSegments("/api") && !caminho.StartsWithSegments("/api/auth/logout"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { mensagem = "Modo demonstração: aqui é só para olhar. 🙂" });
            return;
        }
        await proximo();
    }
}
