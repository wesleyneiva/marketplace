using Marketplace.Api.Models;
using Marketplace.Api.Services;

namespace Marketplace.Api.Seguranca;

// "Porteiro" dos módulos: o que o dono da plataforma não liberou para o cliente (Empresa.Modulos) também não abre
// pela API. Vale na hora: ao salvar os módulos o cache (LocaisDasLojas) é esquecido.
// Vendas, Etiquetas e Fornecedores só somem da tela: a API deles é a mesma do caixa, dos produtos e das compras.
public static class ModulosLiberados
{
    private static readonly (string Caminho, string Modulo)[] Rotas =
    [
        ("/api/compras", Modulos.Compras),
        ("/api/contas", Modulos.Contas),
        ("/api/relatorios", Modulos.Relatorios),
        ("/api/produtos/importacao", Modulos.Importacao),
        ("/api/fiado", Modulos.Fiado),
        ("/api/financeiro", Modulos.Financeiro),
    ];

    public static async Task Middleware(HttpContext ctx, Func<Task> proximo)
    {
        var caminho = ctx.Request.Path;
        var empresaId = ctx.RequestServices.GetRequiredService<ContextoEmpresa>().EmpresaId;
        if (empresaId is { } id && caminho.StartsWithSegments("/api"))
        {
            var liberados = ctx.RequestServices.GetRequiredService<LocaisDasLojas>().Modulos(id);
            foreach (var (prefixo, modulo) in Rotas)
            {
                if (!caminho.StartsWithSegments(prefixo) || Modulos.Liberado(liberados, modulo)) continue;
                // Contas a pagar usa as contas e as categorias do financeiro (só para ler) mesmo sem o módulo Financeiro.
                if (modulo == Modulos.Financeiro && HttpMethods.IsGet(ctx.Request.Method)
                    && (caminho.StartsWithSegments("/api/financeiro/contas") || caminho.StartsWithSegments("/api/financeiro/categorias")))
                    continue;
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { mensagem = "Este módulo não está liberado no seu plano. Fale com a WN Labs." });
                return;
            }
        }
        await proximo();
    }
}
