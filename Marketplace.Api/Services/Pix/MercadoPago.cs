using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marketplace.Api.Services.Pix;

// Conversa com a API do Mercado Pago (https://api.mercadopago.com) usando o Access Token DA LOJA.
// Só três coisas: criar um Pix (QR único com o valor), consultar um pagamento e cancelar um Pix que não foi pago.
public class MercadoPago(IHttpClientFactory fabrica, IConfiguration config, ILogger<MercadoPago> log)
{
    // Endereço da API (MercadoPago:Url só muda nos testes, com o simulador local).
    private string Base => (config["MercadoPago:Url"] ?? "https://api.mercadopago.com").TrimEnd('/');

    public sealed record Pagamento(string Id, string Status, string? Detalhe, decimal Valor, string? Referencia, DateTimeOffset? AprovadoEm, string? CopiaECola);
    public sealed record Conta(string Nome, string? Email, bool Teste);

    // GET /users/me → confere o token e diz de quem é a conta.
    public async Task<Conta> ContaAsync(string token, CancellationToken ct = default)
    {
        var j = await EnviarAsync(HttpMethod.Get, "/users/me", token, null, null, ct);
        var nome = j["nickname"]?.GetValue<string>() ?? j["first_name"]?.GetValue<string>() ?? "conta Mercado Pago";
        var tags = j["tags"]?.AsArray().Select(t => t?.GetValue<string>()).ToList() ?? [];
        return new Conta(nome, j["email"]?.GetValue<string>(), tags.Contains("test_user") || nome.StartsWith("TESTUSER", StringComparison.OrdinalIgnoreCase));
    }

    // POST /v1/payments com payment_method_id = pix → o QR Code (copia e cola) desta venda.
    public async Task<Pagamento> CriarPixAsync(string token, decimal valor, string descricao, Guid referencia, DateTimeOffset expira,
        string? avisoUrl, string emailPagador, bool teste, CancellationToken ct = default)
    {
        var corpo = new JsonObject
        {
            ["transaction_amount"] = valor,
            ["description"] = descricao,
            ["payment_method_id"] = "pix",
            ["external_reference"] = referencia.ToString(),
            ["date_of_expiration"] = expira.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            // No modo de teste do Mercado Pago, o nome "APRO" faz o Pix ser aprovado sozinho (para testar sem dinheiro).
            // Teste: o comprador TEM de ser o de teste (test@testuser.com); com e-mail "de verdade" o Mercado Pago recusa
            // com "Unauthorized use of live credentials" (mistura de conta de teste com conta real).
            ["payer"] = new JsonObject { ["email"] = teste ? "test@testuser.com" : emailPagador, ["first_name"] = teste ? "APRO" : "Cliente" },
        };
        if (avisoUrl is not null) corpo["notification_url"] = avisoUrl;
        // A mesma referência = o mesmo Pix: se a rede cair e o Marcus tentar de novo, o Mercado Pago não cria dois.
        var j = await EnviarAsync(HttpMethod.Post, "/v1/payments", token, corpo, referencia.ToString(), ct);
        return Ler(j);
    }

    public async Task<Pagamento> ConsultarAsync(string token, string id, CancellationToken ct = default) =>
        Ler(await EnviarAsync(HttpMethod.Get, $"/v1/payments/{Uri.EscapeDataString(id)}", token, null, null, ct));

    public async Task<Pagamento> CancelarAsync(string token, string id, CancellationToken ct = default) =>
        Ler(await EnviarAsync(HttpMethod.Put, $"/v1/payments/{Uri.EscapeDataString(id)}", token, new JsonObject { ["status"] = "cancelled" }, null, ct));

    private static Pagamento Ler(JsonNode j) => new(
        j["id"]?.ToString() ?? throw new MercadoPagoException("O Mercado Pago não devolveu o número do pagamento."),
        j["status"]?.GetValue<string>() ?? "",
        j["status_detail"]?.GetValue<string>(),
        j["transaction_amount"]?.GetValue<decimal>() ?? 0,
        j["external_reference"]?.GetValue<string>(),
        DateTimeOffset.TryParse(j["date_approved"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var a) ? a : null,
        j["point_of_interaction"]?["transaction_data"]?["qr_code"]?.GetValue<string>());

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

public class MercadoPagoException(string mensagem) : Exception(mensagem);
