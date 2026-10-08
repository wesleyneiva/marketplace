using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marketplace.Api.Seguranca;

// Protege endpoints usados por OUTROS SISTEMAS (como o n8n), que não fazem login com cookie.
// Quem chama precisa mandar o cabeçalho  X-Api-Key: <chave>  igual ao "Integracao:ChaveApi" da configuração.
// Uso: [ChaveApi] em cima do controller.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ChaveApiAttribute : Attribute, IAuthorizationFilter
{
    public const string Cabecalho = "X-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configurada = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Integracao:ChaveApi"];
        if (string.IsNullOrWhiteSpace(configurada))
        {
            context.Result = new ObjectResult(new { mensagem = "Integração não configurada neste servidor." }) { StatusCode = 503 };
            return;
        }

        var recebida = context.HttpContext.Request.Headers[Cabecalho].ToString();
        // Comparação em "tempo constante": não dá pistas, pelo tempo de resposta, de quantas letras acertou.
        var iguais = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebida), Encoding.UTF8.GetBytes(configurada));
        if (!iguais)
            context.Result = new UnauthorizedObjectResult(new { mensagem = "Chave de API inválida." });
    }
}
