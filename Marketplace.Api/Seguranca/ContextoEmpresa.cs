using System.Security.Claims;
using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Seguranca;

// "De qual empresa é este pedido?" — um por requisição (Scoped).
//  • Pessoa logada: vem do cookie (claim "empresa"), colocado no login.
//  • n8n (X-Api-Key): a empresa da configuração Integracao:EmpresaId.
//  • Trabalhos em segundo plano (simulador, sementes): definida no código, com Definir().
// Sem empresa definida, as consultas às tabelas da empresa voltam VAZIAS e gravar dá erro:
// na dúvida, o sistema fecha a porta (em vez de mostrar dados de todo mundo).
public class ContextoEmpresa
{
    public const string Claim = "empresa";

    public int? EmpresaId { get; private set; }

    public void Definir(int empresaId)
    {
        if (EmpresaId is not null && EmpresaId != empresaId)
            throw new InvalidOperationException("A empresa desta requisição já foi definida (e é outra).");
        EmpresaId = empresaId;
    }

    // Depois do login (UseAuthentication): lê a empresa do cookie. Cookies antigos (de antes do
    // multi-tenant) não têm a claim: busca no banco — em até 1 minuto o cookie é renovado com ela.
    public static async Task Middleware(HttpContext ctx, Func<Task> proximo)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var contexto = ctx.RequestServices.GetRequiredService<ContextoEmpresa>();
            if (int.TryParse(ctx.User.FindFirstValue(Claim), out var empresaId))
                contexto.Definir(empresaId);
            else
            {
                var usuarioId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var db = ctx.RequestServices.GetRequiredService<AppDbContext>();
                var doBanco = await db.Users.IgnoreQueryFilters().Where(u => u.Id == usuarioId)
                    .Select(u => (int?)u.EmpresaId).FirstOrDefaultAsync();
                if (doBanco is { } id) contexto.Definir(id);
            }
        }
        await proximo();
    }
}

public static class EscoposDaEmpresa
{
    // Para trabalhos em segundo plano: um "escopo" (DbContext novo) já com a empresa definida.
    public static IServiceScope CriarEscopo(this IServiceScopeFactory escopos, int empresaId)
    {
        var scope = escopos.CreateScope();
        scope.ServiceProvider.GetRequiredService<ContextoEmpresa>().Definir(empresaId);
        return scope;
    }

    public static IServiceScope CriarEscopo(this IServiceProvider services, int empresaId) =>
        services.GetRequiredService<IServiceScopeFactory>().CriarEscopo(empresaId);
}
