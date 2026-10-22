using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marketplace.Api.Services.Pix;

// Pix automático DIRETO PELO BANCO DA LOJA, pela "API Pix" padrão do Banco Central (a mesma em todos os bancos; muda o
// endereço, o login e pequenos detalhes, que ficam no Perfil). O dinheiro cai na conta da loja, sem intermediário.
//   PUT   /cob/{txid}                      cria a cobrança (QR único com o valor, expira em N segundos)
//   GET   /cob/{txid}                      situação: ATIVA, CONCLUIDA (com a lista "pix" recebida), REMOVIDA_…
//   PATCH /cob/{txid}                      cancela (status REMOVIDA_PELO_USUARIO_RECEBEDOR)
//   PUT   /pix/{e2eid}/devolucao/{id}      devolve o Pix recebido ao cliente
//   GET   /cob?inicio=&fim=                lista (só para conferir as credenciais ao ligar)
// Login: OAuth2 client_credentials (client_id e client_secret do portal do banco). Sem aviso (webhook): o padrão exige
// mTLS no NOSSO lado, que o túnel da Cloudflare não repassa — o caixa pergunta a cada 3 s, como já faz com o Mercado Pago.
//
// Banco do Brasil (22/10/2026, montado pelo padrão + notas de integradores; CONFERIR no portal developers.bb.com.br
// e no ambiente de testes antes do 1º cliente): chave do desenvolvedor vai NA URL (?gw-dev-app-key=…), não no
// cabeçalho; o QR pode vir em "textoImagemQRcode" em vez de "pixCopiaECola"; produção exige o certificado digital da
// empresa (mTLS, .pfx). Teste local sem conta: ~/projetos/marketplace-testes/pix-automatico/simulador-bb.py
// (PixBanco:BancoDoBrasil:UrlApi e :UrlToken apontam para ele).
public class PixBancoCentral(IHttpClientFactory fabrica, IConfiguration config, ILogger<PixBancoCentral> log)
{
    public sealed record Perfil(string Codigo, string Nome, string UrlApiTeste, string UrlApiProducao, string UrlTokenTeste,
        string UrlTokenProducao, string? ParamChaveTeste, string? ParamChaveProducao, string Escopos);

    public static readonly Perfil BancoDoBrasil = new("BancoDoBrasil", "Banco do Brasil",
        "https://api.hm.bb.com.br/pix/v2", "https://api-pix.bb.com.br/pix/v2",
        "https://oauth.hm.bb.com.br/oauth/token", "https://oauth.bb.com.br/oauth/token",
        "gw-dev-app-key", "gw-dev-app-key", "cob.write cob.read pix.write pix.read");

    public static Perfil? PerfilDe(string? codigo) => codigo == BancoDoBrasil.Codigo ? BancoDoBrasil : null;

    // Tudo o que a loja cadastra (Configurações → Pix da loja). Certificado: o .pfx da empresa, para produção.
    public sealed record Credenciais(string ClientId, string ClientSecret, string? ChaveApp, bool Teste, byte[]? Pfx, string? SenhaPfx);

    // ================================================================ operações

    // Confere as credenciais (login + uma consulta) antes de guardar.
    public async Task TestarAsync(Perfil p, Credenciais c, CancellationToken ct = default)
    {
        var fim = DateTimeOffset.UtcNow;
        var faixa = $"?inicio={Uri.EscapeDataString(fim.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"))}&fim={Uri.EscapeDataString(fim.ToString("yyyy-MM-ddTHH:mm:ssZ"))}";
        // O BB responde 404 "Nenhum resultado encontrado" quando não há cobranças no período (em vez de uma lista vazia):
        // o login e a chave passaram, então está certo (visto no ambiente de testes real em 22/10/2026).
        try { await EnviarAsync(p, c, HttpMethod.Get, "/cob" + faixa, null, ct); }
        catch (BancoPixException e) when (e.Codigo == HttpStatusCode.NotFound) { }
    }

    // PUT /cob/{txid}: o txid é a NOSSA referência (32 letras/números). A mesma referência = a mesma cobrança: se a rede
    // cair e o Marcus tentar de novo, o banco recusa a repetida (409) e a gente só consulta.
    public async Task<PagamentoPix> CriarAsync(Perfil p, Credenciais c, string chave, decimal valor, string descricao, Guid referencia,
        TimeSpan validade, CancellationToken ct = default)
    {
        var txid = Txid(referencia);
        var corpo = new JsonObject
        {
            ["calendario"] = new JsonObject { ["expiracao"] = (int)validade.TotalSeconds },
            ["valor"] = new JsonObject { ["original"] = valor.ToString("0.00", CultureInfo.InvariantCulture) },
            ["chave"] = chave,
            ["solicitacaoPagador"] = descricao.Length <= 140 ? descricao : descricao[..140],
        };
        try { return Ler(await EnviarAsync(p, c, HttpMethod.Put, $"/cob/{txid}", corpo, ct), txid); }
        catch (BancoPixException e) when (e.Codigo == HttpStatusCode.Conflict) { return await ConsultarAsync(p, c, txid, ct); }
    }

    public async Task<PagamentoPix> ConsultarAsync(Perfil p, Credenciais c, string txid, CancellationToken ct = default) =>
        Ler(await EnviarAsync(p, c, HttpMethod.Get, $"/cob/{Validar(txid)}", null, ct), txid);

    public async Task<PagamentoPix> CancelarAsync(Perfil p, Credenciais c, string txid, CancellationToken ct = default) =>
        Ler(await EnviarAsync(p, c, HttpMethod.Patch, $"/cob/{Validar(txid)}", new JsonObject { ["status"] = "REMOVIDA_PELO_USUARIO_RECEBEDOR" }, ct), txid);

    // PUT /pix/{e2eid}/devolucao/{id}: o id da devolução é fixo por cobrança — se a rede cair e o Marcus tentar de novo,
    // é a MESMA devolução (o banco não devolve duas vezes). Devolve o valor inteiro.
    public async Task DevolverAsync(Perfil p, Credenciais c, string endToEndId, string idDevolucao, decimal valor, CancellationToken ct = default)
    {
        var corpo = new JsonObject { ["valor"] = valor.ToString("0.00", CultureInfo.InvariantCulture) };
        var j = await EnviarAsync(p, c, HttpMethod.Put, $"/pix/{Validar(endToEndId)}/devolucao/{Validar(idDevolucao)}", corpo, ct);
        var status = j["status"]?.GetValue<string>();
        if (status == "NAO_REALIZADO")
            throw new BancoPixException($"O banco não fez a devolução{(j["motivo"]?.GetValue<string>() is { Length: > 0 } m ? $" ({m})" : "")}.");
        // DEVOLVIDO ou EM_PROCESSAMENTO (o banco termina sozinho em segundos): para o Marcus, já foi pedido e não se repete.
    }

    public static string Txid(Guid referencia) => referencia.ToString("N");

    // ================================================================ resposta → idioma comum

    // ATIVA = esperando; CONCLUIDA = pago (confere a lista "pix": soma do recebido); com devolução DEVOLVIDO = devolvido;
    // REMOVIDA_PELO_USUARIO_RECEBEDOR = cancelado por nós; REMOVIDA_PELO_PSP = o banco tirou (expirou).
    private static PagamentoPix Ler(JsonNode j, string txid)
    {
        var status = j["status"]?.GetValue<string>() ?? "";
        var original = Valor(j["valor"]?["original"]);
        var pix = j["pix"]?.AsArray().Where(x => x is not null).Select(x => x!).ToList() ?? [];
        var recebido = pix.Sum(x => Valor(x["valor"]));
        var devolvido = pix.SelectMany(x => x["devolucoes"]?.AsArray() ?? []).Where(d => d?["status"]?.GetValue<string>() is "DEVOLVIDO" or "EM_PROCESSAMENTO")
            .Sum(d => Valor(d!["valor"]));
        var normal = status switch
        {
            "CONCLUIDA" when pix.Count > 0 && devolvido > 0 && devolvido >= recebido - 0.005m => "refunded",
            "CONCLUIDA" when pix.Count > 0 => "approved",
            "REMOVIDA_PELO_USUARIO_RECEBEDOR" => "cancelled",
            "REMOVIDA_PELO_PSP" => "expired",
            _ => "pending", // ATIVA, ou CONCLUIDA sem a lista "pix" ainda (não liquida sem ver o Pix)
        };
        var primeiro = pix.FirstOrDefault();
        DateTimeOffset? horario = DateTimeOffset.TryParse(primeiro?["horario"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) ? h : null;
        return new PagamentoPix(
            j["txid"]?.GetValue<string>() ?? txid,
            normal, status,
            normal is "approved" or "refunded" ? recebido : original,
            Guid.TryParseExact(j["txid"]?.GetValue<string>() ?? txid, "N", out var g) ? g.ToString() : null,
            horario,
            j["pixCopiaECola"]?.GetValue<string>() ?? j["textoImagemQRcode"]?.GetValue<string>(),
            primeiro?["endToEndId"]?.GetValue<string>());
    }

    private static decimal Valor(JsonNode? n) =>
        decimal.TryParse(n?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;

    // txid, E2E e id de devolução: só letras e números (o padrão do Banco Central), nunca vão "crus" na URL.
    private static string Validar(string id) =>
        id.Length is > 0 and <= 40 && id.All(char.IsAsciiLetterOrDigit) ? id : throw new BancoPixException("Identificador de Pix inválido.");

    // ================================================================ conversa com o banco

    private async Task<JsonNode> EnviarAsync(Perfil p, Credenciais c, HttpMethod metodo, string caminho, JsonNode? corpo, CancellationToken ct, bool novaTentativa = true)
    {
        var token = await TokenAsync(p, c, ct);
        var url = UrlApi(p, c) + caminho;
        if (ParamChave(p, c) is { } param && !string.IsNullOrEmpty(c.ChaveApp))
            url += (caminho.Contains('?') ? "&" : "?") + param + "=" + Uri.EscapeDataString(c.ChaveApp);
        using var pedido = new HttpRequestMessage(metodo, url);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo is not null) pedido.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
        var (codigo, texto) = await MandarAsync(c, pedido, ct);
        if ((int)codigo is >= 200 and < 300) return JsonNode.Parse(texto.Length == 0 ? "{}" : texto) ?? new JsonObject();

        // Token recusado no meio do caminho (o banco expirou antes): pega outro e tenta uma vez.
        if (codigo == HttpStatusCode.Unauthorized && novaTentativa)
        {
            tokens.TryRemove(ChaveCache(p, c), out _);
            return await EnviarAsync(p, c, metodo, caminho, corpo, ct, novaTentativa: false);
        }
        // Nunca loga a URL inteira: leva a chave do desenvolvedor.
        log.LogWarning("Pix {Banco} {Metodo} {Caminho}: {Codigo} {Texto}", p.Nome, metodo, caminho.Split('?')[0], (int)codigo, texto.Length > 400 ? texto[..400] : texto);
        throw new BancoPixException((int)codigo switch
        {
            401 or 403 => $"O {p.Nome} recusou o acesso" + (Mensagem(texto) is { } m401 ? $" ({m401})" : "") +
                          ": confira o client_id, o client_secret, a chave do desenvolvedor e se a API Pix está liberada para a aplicação.",
            404 => $"Cobrança Pix não encontrada no {p.Nome}.",
            _ => $"O {p.Nome} recusou o pedido" + (Mensagem(texto) is { } m ? $": {m}" : "."),
        }, codigo);
    }

    // Token do OAuth, guardado até 1 minuto antes de vencer (o caixa pergunta a cada 3 s: não dá para pedir login toda vez).
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Vence)> tokens = new();

    private async Task<string> TokenAsync(Perfil p, Credenciais c, CancellationToken ct)
    {
        var chave = ChaveCache(p, c);
        if (tokens.TryGetValue(chave, out var t) && t.Vence > DateTimeOffset.UtcNow) return t.Token;
        using var pedido = new HttpRequestMessage(HttpMethod.Post, UrlToken(p, c));
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.ClientId}:{c.ClientSecret}")));
        pedido.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = p.Escopos });
        var (codigo, texto) = await MandarAsync(c, pedido, ct);
        if (!((int)codigo is >= 200 and < 300))
        {
            log.LogWarning("Pix {Banco} login: {Codigo} {Texto}", p.Nome, (int)codigo, texto.Length > 300 ? texto[..300] : texto);
            throw new BancoPixException($"O {p.Nome} recusou o login da aplicação ({(int)codigo}): confira o client_id e o client_secret" +
                                        (c.Teste ? " (do ambiente de TESTES)." : " (de PRODUÇÃO)."), codigo);
        }
        var j = JsonNode.Parse(texto);
        var token = j?["access_token"]?.GetValue<string>() ?? throw new BancoPixException($"O {p.Nome} não devolveu o token de acesso.");
        var segundos = int.TryParse(j?["expires_in"]?.ToString(), out var s) ? s : 600;
        tokens[chave] = (token, DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, segundos - 60)));
        return token;
    }

    private async Task<(HttpStatusCode, string)> MandarAsync(Credenciais c, HttpRequestMessage pedido, CancellationToken ct)
    {
        try
        {
            var http = Cliente(c);
            using var resposta = await http.SendAsync(pedido, ct);
            return (resposta.StatusCode, await resposta.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException e) when (e.InnerException is AuthenticationException)
        {
            throw new BancoPixException(c.Pfx is null
                ? "O banco recusou a conexão segura: em produção ele exige o certificado digital da empresa (.pfx). Envie em Configurações → Pix da loja."
                : "O banco recusou o certificado digital: confira se é o da empresa (e-CNPJ A1), se não venceu e se foi cadastrado no portal do banco.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new BancoPixException("O banco não respondeu. Confira a internet e tente de novo.");
        }
    }

    // Sem certificado: o cliente comum. Com certificado (mTLS): um cliente por certificado, guardado (abrir conexão
    // segura a cada 3 s seria lento).
    private static readonly ConcurrentDictionary<string, HttpClient> clientesComCertificado = new();

    private HttpClient Cliente(Credenciais c)
    {
        if (c.Pfx is null)
        {
            var http = fabrica.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            return http;
        }
        var chave = Convert.ToHexString(SHA256.HashData(c.Pfx));
        return clientesComCertificado.GetOrAdd(chave, _ =>
        {
            var certificado = AbrirCertificado(c.Pfx, c.SenhaPfx);
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            handler.SslOptions.ClientCertificates = [certificado];
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        });
    }

    // Abre o .pfx (confere a senha) e diz de quem é e até quando vale — a tela mostra antes de ligar.
    public static X509Certificate2 AbrirCertificado(byte[] pfx, string? senha)
    {
        try { return X509CertificateLoader.LoadPkcs12(pfx, senha, X509KeyStorageFlags.EphemeralKeySet); }
        catch (CryptographicException) { throw new BancoPixException("Não consegui abrir o certificado: confira o arquivo (.pfx) e a senha dele."); }
    }

    private string UrlApi(Perfil p, Credenciais c) =>
        (config[$"PixBanco:{p.Codigo}:UrlApi"] ?? (c.Teste ? p.UrlApiTeste : p.UrlApiProducao)).TrimEnd('/');
    private string UrlToken(Perfil p, Credenciais c) =>
        config[$"PixBanco:{p.Codigo}:UrlToken"] ?? (c.Teste ? p.UrlTokenTeste : p.UrlTokenProducao);
    private string? ParamChave(Perfil p, Credenciais c) =>
        c.Teste ? p.ParamChaveTeste : config[$"PixBanco:{p.Codigo}:ParamChaveProducao"] ?? p.ParamChaveProducao;
    private static string ChaveCache(Perfil p, Credenciais c) =>
        p.Codigo + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{c.ClientId}|{c.ClientSecret}|{c.Teste}")));

    // Erro no padrão do Banco Central ({title, detail, violacoes:[{razao}]}) ou do jeito do banco ({erros:[{mensagem}]}).
    private static string? Mensagem(string texto)
    {
        try
        {
            var j = JsonNode.Parse(texto);
            return j?["violacoes"]?.AsArray().FirstOrDefault()?["razao"]?.GetValue<string>()
                   ?? j?["detail"]?.GetValue<string>()
                   ?? j?["erros"]?.AsArray().FirstOrDefault()?["mensagem"]?.GetValue<string>()
                   ?? j?["title"]?.GetValue<string>()
                   ?? j?["error_description"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return null; }
    }
}

public class BancoPixException(string mensagem, HttpStatusCode? codigo = null) : ProvedorPixException(mensagem)
{
    public HttpStatusCode? Codigo { get; } = codigo;
}
