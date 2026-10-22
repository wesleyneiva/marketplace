using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marketplace.Api.Services.Pix;

// Conversa com a API do Mercado Pago (https://api.mercadopago.com) usando o Access Token DA LOJA.
// Criar um Pix (QR único com o valor), consultar, cancelar um Pix que não foi pago e devolver um Pix pago (API de Orders).
public class MercadoPago(IHttpClientFactory fabrica, IConfiguration config, ILogger<MercadoPago> log)
{
    // Endereço da API (MercadoPago:Url só muda nos testes, com o simulador local).
    private string Base => (config["MercadoPago:Url"] ?? "https://api.mercadopago.com").TrimEnd('/');

    public sealed record Conta(string Nome, string? Email, bool Teste);

    // GET /users/me → confere o token e diz de quem é a conta.
    public async Task<Conta> ContaAsync(string token, CancellationToken ct = default)
    {
        var j = await EnviarAsync(HttpMethod.Get, "/users/me", token, null, null, ct);
        var nome = j["nickname"]?.GetValue<string>() ?? j["first_name"]?.GetValue<string>() ?? "conta Mercado Pago";
        var tags = j["tags"]?.AsArray().Select(t => t?.GetValue<string>()).ToList() ?? [];
        return new Conta(nome, j["email"]?.GetValue<string>(), tags.Contains("test_user") || nome.StartsWith("TESTUSER", StringComparison.OrdinalIgnoreCase));
    }

    // POST /v1/orders com um pagamento Pix (API de Orders, a atual do Mercado Pago; é a que aceita as credenciais de teste).
    public async Task<PagamentoPix> CriarPixAsync(string token, decimal valor, string descricao, Guid referencia, TimeSpan validade,
        string emailPagador, bool teste, CancellationToken ct = default)
    {
        var total = valor.ToString("0.00", CultureInfo.InvariantCulture);
        var corpo = new JsonObject
        {
            ["type"] = "online",
            ["processing_mode"] = "automatic",
            ["total_amount"] = total,
            ["external_reference"] = referencia.ToString(),
            ["description"] = descricao,
            ["transactions"] = new JsonObject
            {
                ["payments"] = new JsonArray(new JsonObject
                {
                    ["amount"] = total,
                    ["payment_method"] = new JsonObject { ["id"] = "pix", ["type"] = "bank_transfer" },
                    ["expiration_time"] = $"PT{(int)Math.Ceiling(validade.TotalMinutes)}M", // duração ISO 8601 (mínimo 30 min)
                }),
            },
            // Teste: comprador de teste (test@testuser.com) e nome "APRO" → o Mercado Pago aprova sozinho em poucos segundos.
            ["payer"] = new JsonObject { ["email"] = teste ? "test@testuser.com" : emailPagador, ["first_name"] = teste ? "APRO" : "Cliente" },
        };
        // A mesma referência = o mesmo Pix: se a rede cair e o Marcus tentar de novo, o Mercado Pago não cria dois.
        return Ler(await EnviarAsync(HttpMethod.Post, "/v1/orders", token, corpo, referencia.ToString(), ct));
    }

    public async Task<PagamentoPix> ConsultarAsync(string token, string id, CancellationToken ct = default) =>
        Ler(await EnviarAsync(HttpMethod.Get, $"/v1/orders/{Uri.EscapeDataString(id)}", token, null, null, ct));

    public async Task<PagamentoPix> CancelarAsync(string token, string id, CancellationToken ct = default) =>
        Ler(await EnviarAsync(HttpMethod.Post, $"/v1/orders/{Uri.EscapeDataString(id)}/cancel", token, null, Guid.NewGuid().ToString(), ct));

    // POST /v1/orders/{id}/refund sem corpo = devolução TOTAL ao cliente (estorno de venda cancelada).
    // A chave de idempotência é fixa por cobrança: se a rede cair e o Marcus tentar de novo, o Mercado Pago não devolve duas vezes.
    public async Task<PagamentoPix> DevolverAsync(string token, string id, string idempotencia, CancellationToken ct = default) =>
        Ler(await EnviarAsync(HttpMethod.Post, $"/v1/orders/{Uri.EscapeDataString(id)}/refund", token, null, idempotencia, ct));

    // Status da order → o "idioma" de sempre: processed/accredited = pago (approved); expired; refunded = devolvido; canceled/failed = cancelado.
    private static PagamentoPix Ler(JsonNode j)
    {
        var status = j["status"]?.GetValue<string>() ?? "";
        var detalhe = j["status_detail"]?.GetValue<string>();
        var pagamento = j["transactions"]?["payments"]?.AsArray().FirstOrDefault();
        var normal = status switch
        {
            "processed" => "approved",
            "expired" => "expired",
            "refunded" => "refunded",
            "canceled" or "failed" or "charged_back" => "cancelled",
            _ => "pending",
        };
        return new PagamentoPix(
            j["id"]?.ToString() ?? throw new MercadoPagoException("O Mercado Pago não devolveu o número do pedido."),
            normal, detalhe,
            decimal.TryParse(j["total_amount"]?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0,
            j["external_reference"]?.GetValue<string>(),
            null,
            pagamento?["payment_method"]?["qr_code"]?.GetValue<string>());
    }

    private async Task<JsonNode> EnviarAsync(HttpMethod metodo, string caminho, string token, JsonNode? corpo, string? idempotencia, CancellationToken ct)
    {
        using var http = fabrica.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        using var pedido = new HttpRequestMessage(metodo, Base + caminho);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencia is not null) pedido.Headers.Add("X-Idempotency-Key", idempotencia);
        if (corpo is not null) pedido.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage resposta;
        try { resposta = await http.SendAsync(pedido, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new MercadoPagoException("O Mercado Pago não respondeu. Confira a internet e tente de novo.");
        }
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (resposta.IsSuccessStatusCode) return JsonNode.Parse(texto) ?? new JsonObject();

        log.LogWarning("Mercado Pago {Metodo} {Caminho}: {Codigo} {Texto}", metodo, caminho, (int)resposta.StatusCode, texto.Length > 400 ? texto[..400] : texto);
        throw new MercadoPagoException((int)resposta.StatusCode switch
        {
            401 or 403 when Mensagem(texto) is "Unauthorized use of live credentials" =>
                "O Mercado Pago recusou: credenciais de produção ainda não ativadas, ou mistura de teste com produção. Confira em Configurações → Pix da loja se o \"modo teste\" bate com o token.",
            401 or 403 => "O Mercado Pago recusou o token de acesso" + (Mensagem(texto) is { } m401 ? $" ({m401})" : "") + ": confira o Access Token em Configurações → Pix da loja.",
            404 => "Pagamento não encontrado no Mercado Pago.",
            _ => "O Mercado Pago recusou o pedido" + (Mensagem(texto) is { } m ? $": {m}" : "."),
        });
    }

    private static string? Mensagem(string texto)
    {
        try { return JsonNode.Parse(texto)?["message"]?.GetValue<string>(); }
        catch (JsonException) { return null; }
    }
}

public class MercadoPagoException(string mensagem) : ProvedorPixException(mensagem);
