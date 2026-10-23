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
// Cada banco muda pouco do padrão, e o que muda fica no Perfil: login no cabeçalho (Basic) ou no corpo (formulário ou
// JSON), sem secret,
// um campo a mais (na URL ou no cabeçalho), certificado já no ambiente de testes, token fixo no sandbox. Só o BB foi
// testado no ambiente real do banco (22/10/2026); os outros (Inter, Sicoob, Itaú, Bradesco, Santander, Sicredi, Efí) foram
// montados pela documentação (23/10/2026) e aparecem
// na tela como "ainda não conferido" até alguém testar com credenciais de verdade — aí vira Conferido = true.
// Teste local sem conta: ~/projetos/marketplace-testes/pix-automatico/simulador-bb.py (BB, http) e
// simulador-bancos.py (os outros sete, https com certificado do cliente); PixBanco:<Codigo>:UrlApi e :UrlToken
// apontam para eles.
public class PixBancoCentral(IHttpClientFactory fabrica, IConfiguration config, IHostEnvironment ambiente, ILogger<PixBancoCentral> log)
{
    public enum Onde { Url, Cabecalho }

    // Campo a mais que o banco pede em toda chamada: a chave do desenvolvedor do BB (na URL), a conta corrente do Inter
    // (no cabeçalho, só quando a aplicação tem mais de uma conta).
    public sealed record CampoExtra(string Nome, Onde Onde, string Rotulo, string Dica, bool Obrigatorio);

    public sealed record Perfil
    {
        public required string Codigo { get; init; }
        public required string Nome { get; init; }
        public required string UrlApiTeste { get; init; }
        public required string UrlApiProducao { get; init; }
        public required string UrlTokenTeste { get; init; }
        public required string UrlTokenProducao { get; init; }
        public string? Escopos { get; init; }                // null: o banco não usa (Itaú)
        public CampoExtra? Extra { get; init; }
        public bool LoginNoCorpo { get; init; }              // client_id/secret no corpo do formulário, não no Basic
        public bool LoginJson { get; init; }                 // Efí: Basic + corpo em JSON {"grant_type": …}, não formulário
        public bool SemSecret { get; init; }                 // Sicoob: só o client_id (quem prova é o certificado)
        public bool ClientIdNoCabecalho { get; init; }       // Sicoob: client_id também em toda chamada da API
        public bool TokenFixoNoTeste { get; init; }          // Sicoob: o sandbox usa um token fixo do portal, sem login
        public bool CertificadoNoTeste { get; init; }        // Inter: até o ambiente de testes pede o certificado
        public required string Certificado { get; init; }   // o que é o certificado desse banco (texto da tela)
        public required string Precisa { get; init; }       // o que a loja precisa ter (texto da tela)
        public bool Conferido { get; init; }                 // testado no ambiente real do banco
    }

    // BB: chave do desenvolvedor NA URL (?gw-dev-app-key=…); o QR vem em "textoImagemQRcode"; 404 ao listar vazio.
    public static readonly Perfil BancoDoBrasil = new()
    {
        Codigo = Models.ProvedoresPix.BancoDoBrasil, Nome = "Banco do Brasil",
        UrlApiTeste = "https://api.hm.bb.com.br/pix/v2", UrlApiProducao = "https://api-pix.bb.com.br/pix/v2",
        UrlTokenTeste = "https://oauth.hm.bb.com.br/oauth/token", UrlTokenProducao = "https://oauth.bb.com.br/oauth/token",
        Escopos = "cob.write cob.read pix.write pix.read",
        Extra = new("gw-dev-app-key", Onde.Url, "Chave do desenvolvedor", "developer_application_key", true),
        Certificado = "e-CNPJ A1 da empresa, arquivo .pfx (com a senha) — a produção do BB exige",
        Precisa = "conta PJ no Banco do Brasil e uma aplicação no portal developers.bb.com.br com a API Pix liberada (o gerente da conta ajuda)",
        Conferido = true,
    };

    // Inter (API Banking v2): login no corpo e com o certificado que o próprio Inter emite (Internet Banking →
    // Soluções para sua empresa → Nova integração: baixa o .crt e o .key), inclusive no sandbox.
    public static readonly Perfil Inter = new()
    {
        Codigo = Models.ProvedoresPix.Inter, Nome = "Inter",
        UrlApiTeste = "https://cdpj-sandbox.partners.uatinter.co/pix/v2", UrlApiProducao = "https://cdpj.partners.bancointer.com.br/pix/v2",
        UrlTokenTeste = "https://cdpj-sandbox.partners.uatinter.co/oauth/v2/token", UrlTokenProducao = "https://cdpj.partners.bancointer.com.br/oauth/v2/token",
        Escopos = "cob.write cob.read pix.write pix.read",
        Extra = new("x-conta-corrente", Onde.Cabecalho, "Conta corrente", "só se a integração tiver mais de uma conta (só números)", false),
        LoginNoCorpo = true, CertificadoNoTeste = true,
        Certificado = "o certificado que o Inter gera na integração: os arquivos .crt e .key (escolha os dois)",
        Precisa = "conta PJ no Inter e uma integração criada no Internet Banking (Soluções para sua empresa → Nova integração) com as permissões de Pix",
    };

    // Sicoob: só client_id (sem secret), certificado e-CNPJ cadastrado na aplicação do developers.sicoob.com.br, e o
    // client_id vai também no cabeçalho de toda chamada. O sandbox usa um token fixo mostrado no portal.
    public static readonly Perfil Sicoob = new()
    {
        Codigo = Models.ProvedoresPix.Sicoob, Nome = "Sicoob",
        UrlApiTeste = "https://sandbox.sicoob.com.br/sicoob/sandbox/pix/api/v2", UrlApiProducao = "https://api.sicoob.com.br/pix/api/v2",
        UrlTokenTeste = "", UrlTokenProducao = "https://auth.sicoob.com.br/auth/realms/cooperado/protocol/openid-connect/token",
        Escopos = "cob.read cob.write pix.read pix.write",
        LoginNoCorpo = true, SemSecret = true, ClientIdNoCabecalho = true, TokenFixoNoTeste = true,
        Certificado = "e-CNPJ A1 da empresa, arquivo .pfx (com a senha), o mesmo enviado na aplicação do portal do Sicoob",
        Precisa = "conta PJ no Sicoob e uma aplicação no portal developers.sicoob.com.br com a API Pix e o certificado da empresa cadastrado",
    };

    // Itaú: login no corpo, sem escopo; certificado emitido pelo próprio Itaú (a loja gera a chave e o pedido, o Itaú
    // assina: .crt + .key, vale 1 ano). O sandbox do portal não pede certificado.
    public static readonly Perfil Itau = new()
    {
        Codigo = Models.ProvedoresPix.Itau, Nome = "Itaú",
        UrlApiTeste = "https://devportal.itau.com.br/sandboxapi/pix_recebimentos_ext_v2/v2", UrlApiProducao = "https://secure.api.itau/pix_recebimentos/v2",
        UrlTokenTeste = "https://devportal.itau.com.br/api/jwt", UrlTokenProducao = "https://sts.itau.com.br/api/oauth/token",
        LoginNoCorpo = true,
        Certificado = "o certificado que o Itaú emite para a aplicação: os arquivos .crt e .key (escolha os dois); vale 1 ano",
        Precisa = "conta PJ no Itaú e a API Pix Recebimentos liberada pelo gerente (o Itaú envia as credenciais e o passo a passo do certificado)",
    };

    // Bradesco: login Basic, sem escopo; certificado da empresa (A1) cadastrado no banco, inclusive na homologação.
    public static readonly Perfil Bradesco = new()
    {
        Codigo = Models.ProvedoresPix.Bradesco, Nome = "Bradesco",
        UrlApiTeste = "https://qrpix-h.bradesco.com.br/v2", UrlApiProducao = "https://qrpix.bradesco.com.br/v2",
        UrlTokenTeste = "https://qrpix-h.bradesco.com.br/auth/server/oauth/token", UrlTokenProducao = "https://qrpix.bradesco.com.br/auth/server/oauth/token",
        CertificadoNoTeste = true,
        Certificado = "e-CNPJ A1 da empresa, arquivo .pfx (com a senha), o mesmo cadastrado no Bradesco",
        Precisa = "conta PJ no Bradesco e a API Pix liberada pelo gerente (o Bradesco cadastra o certificado da empresa e envia o client_id e o client_secret)",
    };

    // Santander: login no corpo (grant_type também na URL, como na documentação); certificado da empresa cuja parte
    // pública vai na aplicação do portal, inclusive na homologação.
    public static readonly Perfil Santander = new()
    {
        Codigo = Models.ProvedoresPix.Santander, Nome = "Santander",
        UrlApiTeste = "https://trust-pix-h.santander.com.br/api/v1", UrlApiProducao = "https://trust-pix.santander.com.br/api/v1",
        UrlTokenTeste = "https://trust-pix-h.santander.com.br/oauth/token?grant_type=client_credentials",
        UrlTokenProducao = "https://trust-pix.santander.com.br/oauth/token?grant_type=client_credentials",
        LoginNoCorpo = true, CertificadoNoTeste = true,
        Certificado = "certificado digital da empresa, arquivo .pfx (com a senha), cuja parte pública foi enviada na aplicação do portal",
        Precisa = "conta PJ no Santander e uma aplicação no portal developer.santander.com.br com a API Pix (o gerente libera)",
    };

    // Sicredi: login Basic com grant_type e escopo na URL; certificado emitido pelo próprio Sicredi (a cooperativa
    // assina o pedido: .cer + .key), inclusive na homologação.
    public static readonly Perfil Sicredi = new()
    {
        Codigo = Models.ProvedoresPix.Sicredi, Nome = "Sicredi",
        UrlApiTeste = "https://api-pix-h.sicredi.com.br/api/v2", UrlApiProducao = "https://api-pix.sicredi.com.br/api/v2",
        UrlTokenTeste = "https://api-pix-h.sicredi.com.br/oauth/token?grant_type=client_credentials&scope=cob.write+cob.read+pix.write+pix.read",
        UrlTokenProducao = "https://api-pix.sicredi.com.br/oauth/token?grant_type=client_credentials&scope=cob.write+cob.read+pix.write+pix.read",
        Escopos = "cob.write cob.read pix.write pix.read", CertificadoNoTeste = true,
        Certificado = "o certificado que o Sicredi emite para a aplicação: os arquivos .cer (ou .crt) e .key (escolha os dois)",
        Precisa = "conta PJ no Sicredi e o acesso à API Pix pedido à cooperativa (ela envia o client_id e o client_secret e emite o certificado)",
    };

    // Efí (ex-Gerencianet): login Basic com corpo em JSON; certificado .p12 gerado na conta Efí (sem senha), um para
    // homologação e outro para produção.
    public static readonly Perfil Efi = new()
    {
        Codigo = Models.ProvedoresPix.Efi, Nome = "Efí",
        UrlApiTeste = "https://pix-h.api.efipay.com.br/v2", UrlApiProducao = "https://pix.api.efipay.com.br/v2",
        UrlTokenTeste = "https://pix-h.api.efipay.com.br/oauth/token", UrlTokenProducao = "https://pix.api.efipay.com.br/oauth/token",
        LoginJson = true, CertificadoNoTeste = true,
        Certificado = "o certificado .p12 gerado na conta Efí (API → Meus certificados; um de homologação e outro de produção, sem senha)",
        Precisa = "conta na Efí (ex-Gerencianet) e uma aplicação em API → Aplicações com os escopos de Pix (cobranças e devoluções)",
    };

    public static readonly IReadOnlyList<Perfil> Bancos = [BancoDoBrasil, Itau, Bradesco, Santander, Inter, Sicoob, Sicredi, Efi];

    public static Perfil? PerfilDe(string? codigo) => Bancos.FirstOrDefault(p => p.Codigo == codigo);

    // Tudo o que a loja cadastra (Configurações → Pix da loja). ChaveApp = o valor do CampoExtra do banco. Certificado:
    // sempre guardado como .pfx (o .crt + .key do Inter/Itaú é convertido ao ligar).
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
            j["pixCopiaECola"]?.GetValue<string>() ?? j["textoImagemQRcode"]?.GetValue<string>() ?? j["brcode"]?.GetValue<string>(),
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
        if (p.Extra?.Onde == Onde.Url && !string.IsNullOrEmpty(c.ChaveApp))
            url += (caminho.Contains('?') ? "&" : "?") + NomeExtra(p, c) + "=" + Uri.EscapeDataString(c.ChaveApp);
        using var pedido = new HttpRequestMessage(metodo, url);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (p.Extra?.Onde == Onde.Cabecalho && !string.IsNullOrEmpty(c.ChaveApp)) pedido.Headers.TryAddWithoutValidation(p.Extra.Nome, c.ChaveApp);
        if (p.ClientIdNoCabecalho) pedido.Headers.TryAddWithoutValidation("client_id", c.ClientId);
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
                          ": confira o client_id, " + (p.SemSecret ? "" : "o client_secret, ") + (p.Extra is { } x ? $"{x.Rotulo.ToLowerInvariant()}, " : "") +
                          "o certificado e se a API Pix está liberada para a aplicação.",
            404 => $"Cobrança Pix não encontrada no {p.Nome}.",
            _ => $"O {p.Nome} recusou o pedido" + (Mensagem(texto) is { } m ? $": {m}" : "."),
        }, codigo);
    }

    // Token do OAuth, guardado até 1 minuto antes de vencer (o caixa pergunta a cada 3 s: não dá para pedir login toda vez).
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Vence)> tokens = new();

    private async Task<string> TokenAsync(Perfil p, Credenciais c, CancellationToken ct)
    {
        if (c.Teste && p.TokenFixoNoTeste) return c.ClientSecret; // sandbox do Sicoob: o "secret" é o token fixo do portal
        var chave = ChaveCache(p, c);
        if (tokens.TryGetValue(chave, out var t) && t.Vence > DateTimeOffset.UtcNow) return t.Token;
        using var pedido = new HttpRequestMessage(HttpMethod.Post, UrlToken(p, c));
        var campos = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };
        if (p.Escopos is not null) campos["scope"] = p.Escopos;
        if (p.LoginNoCorpo)
        {
            campos["client_id"] = c.ClientId;
            if (!p.SemSecret) campos["client_secret"] = c.ClientSecret;
        }
        else pedido.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.ClientId}:{c.ClientSecret}")));
        pedido.Content = p.LoginJson
            ? new StringContent(JsonSerializer.Serialize(campos), Encoding.UTF8, "application/json")
            : new FormUrlEncodedContent(campos);
        var (codigo, texto) = await MandarAsync(c, pedido, ct);
        if (!((int)codigo is >= 200 and < 300))
        {
            log.LogWarning("Pix {Banco} login: {Codigo} {Texto}", p.Nome, (int)codigo, texto.Length > 300 ? texto[..300] : texto);
            throw new BancoPixException($"O {p.Nome} recusou o login da aplicação ({(int)codigo}): confira o client_id" + (p.SemSecret ? ", o certificado" : " e o client_secret") +
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
        // TLS 1.3: o banco recusa o certificado DEPOIS do aperto de mão, então do nosso lado chega só "conexão
        // reiniciada" (visto no simulador, 23/10/2026). Com certificado, o mais provável é ele.
        catch (HttpRequestException e) when (c.Pfx is not null && e.InnerException is IOException)
        {
            log.LogWarning("Pix: o banco fechou a conexão com certificado: {Erro}", string.Join(" → ", Cadeia(e)));
            throw new BancoPixException("O banco fechou a conexão ao receber o certificado: confira se é o certificado desta aplicação " +
                                        "(o que o banco emitiu ou o que foi cadastrado no portal) e se não venceu. Se estiver certo, tente de novo.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning("Pix: conexão com o banco falhou: {Erro}", string.Join(" → ", Cadeia(e)));
            throw new BancoPixException("O banco não respondeu. Confira a internet e tente de novo.");
        }
    }

    private static IEnumerable<string> Cadeia(Exception? e)
    {
        for (; e is not null; e = e.InnerException) yield return $"{e.GetType().Name}: {e.Message}";
    }

    // Sem certificado: o cliente comum. Com certificado (mTLS): um cliente por certificado, guardado (abrir conexão
    // segura a cada 3 s seria lento). Só no desenvolvimento, PixBanco:AceitarServidorDeTeste aceita o certificado
    // próprio do simulador (https local); fora dele a conferência do servidor é sempre a normal.
    private static readonly ConcurrentDictionary<string, HttpClient> clientesComCertificado = new();

    private HttpClient Cliente(Credenciais c)
    {
        var servidorDeTeste = ambiente.IsDevelopment() && config.GetValue<bool>("PixBanco:AceitarServidorDeTeste");
        if (c.Pfx is null && !servidorDeTeste)
        {
            var http = fabrica.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            return http;
        }
        var chave = (c.Pfx is null ? "sem" : Convert.ToHexString(SHA256.HashData(c.Pfx))) + (servidorDeTeste ? ":teste" : "");
        return clientesComCertificado.GetOrAdd(chave, _ =>
        {
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            if (c.Pfx is not null) handler.SslOptions.ClientCertificates = [AbrirCertificado(c.Pfx, c.SenhaPfx)];
            if (servidorDeTeste) handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        });
    }

    // Abre o .pfx (confere a senha) e diz de quem é e até quando vale — a tela mostra antes de ligar.
    public static X509Certificate2 AbrirCertificado(byte[] pfx, string? senha)
    {
        try { return X509CertificateLoader.LoadPkcs12(pfx, senha, X509KeyStorageFlags.EphemeralKeySet); }
        catch (CryptographicException) when (string.IsNullOrEmpty(senha))
        {
            // .p12 "sem senha" (Efí) às vezes é senha vazia, às vezes nenhuma: tenta a outra forma.
            try { return X509CertificateLoader.LoadPkcs12(pfx, senha is null ? "" : null, X509KeyStorageFlags.EphemeralKeySet); }
            catch (CryptographicException) { throw new BancoPixException("Não consegui abrir o certificado: confira o arquivo (.pfx/.p12) e a senha dele."); }
        }
        catch (CryptographicException) { throw new BancoPixException("Não consegui abrir o certificado: confira o arquivo (.pfx/.p12) e a senha dele."); }
    }

    // Inter e Itaú entregam o certificado em dois arquivos de texto (.crt e .key): junta e guarda como .pfx, igual aos
    // outros. A chave pode vir protegida por senha ("ENCRYPTED PRIVATE KEY").
    public static byte[] PfxDePem(string pem, string? senha)
    {
        if (!pem.Contains("-----BEGIN CERTIFICATE-----")) throw new BancoPixException("Falta o arquivo do certificado (.crt).");
        if (!pem.Contains("PRIVATE KEY-----")) throw new BancoPixException("Falta o arquivo da chave do certificado (.key).");
        try
        {
            using var cert = pem.Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----")
                ? X509Certificate2.CreateFromEncryptedPem(pem, pem, senha ?? "")
                : X509Certificate2.CreateFromPem(pem, pem);
            return cert.Export(X509ContentType.Pkcs12);
        }
        catch (CryptographicException)
        {
            throw new BancoPixException("Não consegui juntar o certificado e a chave: confira se o .key é o desse .crt (e a senha, se a chave tiver).");
        }
    }

    private string UrlApi(Perfil p, Credenciais c) =>
        (config[$"PixBanco:{p.Codigo}:UrlApi"] ?? (c.Teste ? p.UrlApiTeste : p.UrlApiProducao)).TrimEnd('/');
    private string UrlToken(Perfil p, Credenciais c) =>
        config[$"PixBanco:{p.Codigo}:UrlToken"] ?? (c.Teste ? p.UrlTokenTeste : p.UrlTokenProducao);
    // BB: na produção talvez seja gw-app-key em vez de gw-dev-app-key (conferir no 1º cliente): dá para trocar sem publicar.
    private string NomeExtra(Perfil p, Credenciais c) =>
        (c.Teste ? null : config[$"PixBanco:{p.Codigo}:ParamChaveProducao"]) ?? p.Extra!.Nome;
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
