using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Pix;

// Pix automático: cria a cobrança (QR único com o valor), acompanha até ser paga e recebe o aviso. O provedor é o
// Mercado Pago ou o BANCO da loja (API Pix do Banco Central, ver PixBancoCentral) — o resto (caixa, venda, devolução)
// é igual para os dois. Cada cobrança guarda o seu provedor: trocar de provedor não atrapalha um Pix já gerado.
// REGRA DE SEGURANÇA: o Marcus só considera um Pix pago depois de PERGUNTAR ao provedor, com as credenciais da loja,
// e conferir valor e referência. O aviso (webhook) só diz "olhe o pagamento X" — nunca é tomado como prova.
public class PixAutomaticoService(AppDbContext db, ContextoEmpresa contexto, MercadoPago mp, PixBancoCentral banco,
    IDataProtectionProvider protecao, IConfiguration config, ILogger<PixAutomaticoService> log)
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30); // mínimo do Mercado Pago (API de Orders)
    private static readonly TimeSpan IntervaloConsulta = TimeSpan.FromSeconds(3);
    private IDataProtector Cofre => protecao.CreateProtector("Marcus.PixAutomatico.MercadoPago.v1");
    private IDataProtector CofreBanco => protecao.CreateProtector("Marcus.PixAutomatico.Banco.v1");

    // ================================================================ configuração (Configurações → Pix da loja)

    public async Task<Empresa> LigarMercadoPagoAsync(string token, bool teste)
    {
        token = token.Trim();
        if (token.Length < 20 || token.Contains(' ')) throw new PixException("Cole o Access Token inteiro (começa com APP_USR- ou TEST-).");
        var conta = await mp.ContaAsync(token); // confere o token antes de guardar
        var e = await db.Empresas.FirstAsync(x => x.Id == contexto.EmpresaId);
        LimparBanco(e);
        e.PixAutomatico = ProvedoresPix.MercadoPago;
        e.MercadoPagoToken = Cofre.Protect(token);
        e.MercadoPagoTokenFinal = token[^4..];
        e.MercadoPagoConta = Cortar(conta.Email is { Length: > 0 } em ? $"{conta.Nome} ({em})" : conta.Nome, 120);
        e.MercadoPagoTeste = teste || conta.Teste;
        e.PixAvisoToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        await db.SaveChangesAsync();
        return e;
    }

    // Banco da loja (Banco do Brasil, Inter, Sicoob, Itaú): confere o login e uma consulta no banco antes de guardar. O QR
    // sai na chave Pix já cadastrada da loja, que precisa ser DESSA conta. Certificado: .pfx (com senha) ou o .crt + .key
    // que o Inter e o Itaú emitem (vira .pfx aqui); a produção de todos exige, o sandbox do Inter também.
    public async Task<Empresa> LigarBancoAsync(string codigoBanco, string clientId, string? clientSecret, string? chaveApp, bool teste,
        byte[]? pfx, string? certificadoPem, string? senhaPfx)
    {
        var perfil = PixBancoCentral.PerfilDe(codigoBanco) ?? throw new PixException("Banco não atendido pela confirmação automática do Pix.");
        var pedeSecret = !perfil.SemSecret || (teste && perfil.TokenFixoNoTeste);
        (clientId, clientSecret, chaveApp) = (clientId.Trim(), pedeSecret ? clientSecret?.Trim() ?? "" : "", string.IsNullOrWhiteSpace(chaveApp) ? null : chaveApp.Trim());
        if (clientId.Length < 8 || clientId.Contains(' ') || (pedeSecret && (clientSecret.Length < 8 || clientSecret.Contains(' '))))
            throw new PixException(perfil.TokenFixoNoTeste && teste
                ? "Cole o client_id e o token de acesso do sandbox inteiros (do portal do banco, sem espaços)."
                : pedeSecret ? "Cole o client_id e o client_secret inteiros (do portal do banco, sem espaços)." : "Cole o client_id inteiro (do portal do banco, sem espaços).");
        if (perfil.Extra is { Obrigatorio: true } x && chaveApp is null) throw new PixException($"Preencha a {x.Rotulo.ToLowerInvariant()}.");
        if (perfil.Extra is null) chaveApp = null;
        var e = await db.Empresas.FirstAsync(x => x.Id == contexto.EmpresaId);
        if (string.IsNullOrEmpty(e.PixChave))
            throw new PixException($"Cadastre antes a chave Pix da loja (acima). Ela precisa ser da conta no {perfil.Nome}: é nela que o dinheiro cai.");

        if (pfx is null && !string.IsNullOrWhiteSpace(certificadoPem))
        {
            pfx = PixBancoCentral.PfxDePem(certificadoPem, senhaPfx);
            senhaPfx = null; // o .pfx montado aqui não tem senha (fica criptografado no cofre como os outros)
        }
        if (pfx is null && (!teste || perfil.CertificadoNoTeste))
            throw new PixException($"O {perfil.Nome} exige o certificado{(teste ? " também no ambiente de testes" : " na produção")}: {perfil.Certificado}.");
        string? info = null;
        if (pfx is { Length: > 20_000 }) throw new PixException("Esse arquivo é grande demais para um certificado .pfx (o A1 tem poucos KB).");
        if (pfx is not null)
        {
            using var cert = PixBancoCentral.AbrirCertificado(pfx, senhaPfx);
            if (cert.NotAfter < DateTime.Now) throw new PixException($"Este certificado venceu em {cert.NotAfter:dd/MM/yyyy}.");
            info = Cortar($"{cert.GetNameInfo(X509NameType.SimpleName, false)} · vence {cert.NotAfter:dd/MM/yyyy}", 200);
        }
        var cred = new PixBancoCentral.Credenciais(clientId, clientSecret, chaveApp, teste, pfx, senhaPfx);
        await banco.TestarAsync(perfil, cred);

        LimparMercadoPago(e);
        e.PixAutomatico = perfil.Codigo;
        e.PixBancoClientId = clientId;
        e.PixBancoSegredos = CofreBanco.Protect(JsonSerializer.Serialize(new SegredosBanco(clientSecret, chaveApp)));
        e.PixBancoCertificado = pfx is null ? null : CofreBanco.Protect(JsonSerializer.Serialize(new CertificadoBanco(Convert.ToBase64String(pfx), senhaPfx)));
        e.PixBancoCertificadoInfo = info;
        e.PixBancoTeste = teste;
        await db.SaveChangesAsync();
        return e;
    }

    private sealed record SegredosBanco(string ClientSecret, string? ChaveApp);
    private sealed record CertificadoBanco(string Pfx, string? Senha);

    public async Task<Empresa> DesligarAsync()
    {
        var e = await db.Empresas.FirstAsync(x => x.Id == contexto.EmpresaId);
        e.PixAutomatico = null;
        LimparMercadoPago(e);
        LimparBanco(e);
        await db.SaveChangesAsync();
        return e;
    }

    private static void LimparMercadoPago(Empresa e) =>
        (e.MercadoPagoToken, e.MercadoPagoTokenFinal, e.MercadoPagoConta, e.MercadoPagoTeste) = (null, null, null, false);
    private static void LimparBanco(Empresa e) =>
        (e.PixBancoClientId, e.PixBancoSegredos, e.PixBancoCertificado, e.PixBancoCertificadoInfo, e.PixBancoTeste) = (null, null, null, null, false);

    // ================================================================ cobrança no caixa

    public async Task<CobrancaPix> CriarAsync(decimal valor, int? sessaoCaixaId, string? usuarioId)
    {
        valor = Math.Round(valor, 2);
        if (valor < 0.01m || valor > 999_999.99m) throw new PixException("Valor do Pix inválido.");
        var e = await db.Empresas.AsNoTracking().FirstAsync(x => x.Id == contexto.EmpresaId);
        if (e.PixAutomatico is null) throw new PixException("A confirmação automática do Pix não está ligada: o administrador liga em Configurações → Pix da loja.");
        if (sessaoCaixaId is int s && !await db.SessoesCaixa.AnyAsync(x => x.Id == s)) throw new PixException("Caixa não encontrado.");

        var c = new CobrancaPix { Provedor = e.PixAutomatico, Valor = valor, ExpiraEm = DateTimeOffset.UtcNow + Validade, SessaoCaixaId = sessaoCaixaId, CriadaPorId = usuarioId };
        var descricao = Cortar($"{e.Nome} · compra no caixa", 120);
        PagamentoPix pg;
        if (PixBancoCentral.PerfilDe(e.PixAutomatico) is { } perfil)
        {
            if (string.IsNullOrEmpty(e.PixChave)) throw new PixException("A loja está sem chave Pix: cadastre em Configurações → Pix da loja.");
            pg = await banco.CriarAsync(perfil, CredenciaisBanco(e), e.PixChave, valor, descricao, c.Referencia, Validade);
        }
        else
            pg = await mp.CriarPixAsync(Token(e), valor, descricao, c.Referencia, Validade,
                e.EmailContato is { Length: > 3 } em && em.Contains('@') ? em : "cliente.pix@wnlabs.com.br", e.MercadoPagoTeste);
        if (string.IsNullOrEmpty(pg.CopiaECola))
            throw new PixException($"O {ProvedoresPix.Nome(c.Provedor)} não devolveu o QR Code. Confira se a conta tem uma chave Pix cadastrada.");
        c.IdExterno = pg.Id;
        c.CopiaECola = pg.CopiaECola;
        c.StatusExterno = pg.Status;
        c.ConsultadaEm = DateTimeOffset.UtcNow;
        db.CobrancasPix.Add(c);
        Aplicar(c, pg);
        await db.SaveChangesAsync();
        return c;
    }

    // Situação atual. Pendente: pergunta ao Mercado Pago (no máximo a cada 3 s, mesmo com vários caixas olhando).
    public async Task<CobrancaPix> SituacaoAsync(int id, bool forcar = false)
    {
        var c = await db.CobrancasPix.FirstOrDefaultAsync(x => x.Id == id) ?? throw new PixException("Cobrança Pix não encontrada.");
        await AtualizarAsync(c, forcar);
        return c;
    }

    public async Task<CobrancaPix> CancelarAsync(int id)
    {
        var c = await db.CobrancasPix.FirstOrDefaultAsync(x => x.Id == id) ?? throw new PixException("Cobrança Pix não encontrada.");
        await AtualizarAsync(c, forcar: true);
        if (c.Status == StatusCobrancaPix.Paga) throw new PixException("Este Pix já foi pago: use-o na venda (ele aparece como Pix recebido).");
        if (c.Status == StatusCobrancaPix.Pendente && c.IdExterno is not null)
        {
            try
            {
                var e = await db.Empresas.AsNoTracking().FirstAsync(x => x.Id == contexto.EmpresaId);
                Aplicar(c, PixBancoCentral.PerfilDe(c.Provedor) is { } perfil
                    ? await banco.CancelarAsync(perfil, CredenciaisBanco(e), c.IdExterno)
                    : await mp.CancelarAsync(Token(e), c.IdExterno));
            }
            catch (Exception ex) when (ex is ProvedorPixException or PixException) { log.LogWarning("Cancelar Pix {Id}: {Erro}", c.Id, ex.Message); }
            if (c.Status == StatusCobrancaPix.Pendente) c.Status = StatusCobrancaPix.Cancelada; // o QR expira sozinho de qualquer jeito
        }
        await db.SaveChangesAsync();
        return c;
    }

    // Pix pagos que ainda não entraram em venda (o caixa fechou a janela, a venda foi abandonada…): o dinheiro está na conta.
    public async Task<List<CobrancaPix>> SoltasAsync(int sessaoCaixaId)
    {
        var desde = DateTimeOffset.UtcNow.AddHours(-24);
        var pendentes = await db.CobrancasPix.Where(c => c.SessaoCaixaId == sessaoCaixaId && c.Status == StatusCobrancaPix.Pendente && c.CriadaEm >= desde).ToListAsync();
        foreach (var c in pendentes) await AtualizarAsync(c, false); // um Pix pago enquanto ninguém olhava
        return await db.CobrancasPix.AsNoTracking()
            .Where(c => c.SessaoCaixaId == sessaoCaixaId && c.Status == StatusCobrancaPix.Paga && c.VendaId == null && c.CriadaEm >= desde)
            .OrderBy(c => c.PagaEm).ToListAsync();
    }

    // Estorno: o provedor devolve o Pix inteiro ao cliente (o dinheiro sai da conta da loja). Quem chama grava.
    // A chave da devolução é fixa por cobrança: se a rede cair e tentar de novo, não devolve duas vezes.
    public async Task DevolverAsync(CobrancaPix c)
    {
        if (c.Status == StatusCobrancaPix.Devolvida) return;
        if (c.Status != StatusCobrancaPix.Paga || c.IdExterno is null) throw new PixException("Este Pix não está pago: não há o que devolver.");
        var e = await db.Empresas.AsNoTracking().FirstAsync(x => x.Id == c.EmpresaId);
        try
        {
            if (PixBancoCentral.PerfilDe(c.Provedor) is { } perfil)
            {
                if (c.EndToEndId is null) throw new PixException("Falta o número do Pix recebido (E2E) para devolver: devolva pelo app do banco.");
                await banco.DevolverAsync(perfil, CredenciaisBanco(e), c.EndToEndId, "D" + PixBancoCentral.Txid(c.Referencia), c.Valor);
                c.StatusExterno = "refunded";
            }
            else c.StatusExterno = Cortar((await mp.DevolverAsync(Token(e), c.IdExterno, $"estorno-{c.Referencia}")).Status, 30);
        }
        catch (ProvedorPixException ex) { throw new PixException($"O {ProvedoresPix.Nome(c.Provedor)} não devolveu o Pix: {ex.Message}"); }
        c.Status = StatusCobrancaPix.Devolvida;
        c.DevolvidaEm = DateTimeOffset.UtcNow;
    }

    // ================================================================ aviso do Mercado Pago (webhook, sem login)

    public async Task<bool> AvisoAsync(string avisoToken, string? pagamentoId)
    {
        var empresa = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.PixAvisoToken == avisoToken);
        if (empresa is null) return false;
        contexto.Definir(empresa.Id);
        if (string.IsNullOrWhiteSpace(pagamentoId)) return true;
        var c = await db.CobrancasPix.FirstOrDefaultAsync(x => x.Provedor == ProvedoresPix.MercadoPago && x.IdExterno == pagamentoId);
        if (c is null) return true; // pagamento que não foi gerado pelo caixa (link, outra loja da conta…)
        await AtualizarAsync(c, forcar: true);
        return true;
    }

    // ================================================================ detalhes

    private async Task AtualizarAsync(CobrancaPix c, bool forcar)
    {
        if (c.Status != StatusCobrancaPix.Pendente || c.IdExterno is null) return;
        var agora = DateTimeOffset.UtcNow;
        if (!forcar && c.ConsultadaEm is { } u && agora - u < IntervaloConsulta) return;
        c.ConsultadaEm = agora;
        try
        {
            var e = await db.Empresas.AsNoTracking().FirstAsync(x => x.Id == c.EmpresaId);
            Aplicar(c, PixBancoCentral.PerfilDe(c.Provedor) is { } perfil
                ? await banco.ConsultarAsync(perfil, CredenciaisBanco(e), c.IdExterno)
                : await mp.ConsultarAsync(Token(e), c.IdExterno));
        }
        catch (Exception ex) when (ex is ProvedorPixException or PixException)
        {
            log.LogWarning("Consultar Pix {Id}: {Erro}", c.Id, ex.Message); // tenta de novo na próxima consulta
        }
        if (c.Status == StatusCobrancaPix.Pendente && agora > c.ExpiraEm + TimeSpan.FromMinutes(2)) c.Status = StatusCobrancaPix.Expirada;
        await db.SaveChangesAsync();
    }

    // O que o provedor disse → situação da cobrança. "Aprovado" só vale com o MESMO valor e a MESMA referência.
    private void Aplicar(CobrancaPix c, PagamentoPix pg)
    {
        c.StatusExterno = Cortar(pg.Status, 30);
        switch (pg.Status)
        {
            case "approved":
                if (pg.Referencia != c.Referencia.ToString() || Math.Abs(pg.Valor - c.Valor) >= 0.005m)
                {
                    log.LogError("Pix {Id}: {Provedor} aprovou com dados diferentes (ref {Ref}, valor {Valor}).", c.Id, c.Provedor, pg.Referencia, pg.Valor);
                    return;
                }
                c.Status = StatusCobrancaPix.Paga;
                c.EndToEndId ??= pg.EndToEndId;
                c.PagaEm = (pg.AprovadoEm ?? DateTimeOffset.UtcNow).ToUniversalTime(); // o banco guarda em UTC (o Mercado Pago manda -03:00/-04:00)
                break;
            case "refunded":
                if (c.Status == StatusCobrancaPix.Paga) { c.Status = StatusCobrancaPix.Devolvida; c.DevolvidaEm ??= DateTimeOffset.UtcNow; }
                else c.Status = StatusCobrancaPix.Cancelada;
                break;
            case "cancelled" or "rejected" or "charged_back":
                c.Status = StatusCobrancaPix.Cancelada;
                break;
            case "expired":
                c.Status = StatusCobrancaPix.Expirada;
                break;
        }
    }

    private string Token(Empresa e)
    {
        if (e.PixAutomatico != ProvedoresPix.MercadoPago || string.IsNullOrEmpty(e.MercadoPagoToken))
            throw new PixException("A confirmação automática do Pix pelo Mercado Pago não está ligada: o administrador liga em Configurações → Pix da loja.");
        try { return Cofre.Unprotect(e.MercadoPagoToken); }
        catch (CryptographicException) { throw new PixException("Não foi possível ler o token do Mercado Pago: cole-o de novo em Configurações → Pix da loja."); }
    }

    private PixBancoCentral.Credenciais CredenciaisBanco(Empresa e)
    {
        if (!ProvedoresPix.EhBanco(e.PixAutomatico) || e.PixBancoClientId is null || e.PixBancoSegredos is null)
            throw new PixException("A confirmação automática do Pix pelo banco não está ligada: o administrador liga em Configurações → Pix da loja.");
        try
        {
            var s = JsonSerializer.Deserialize<SegredosBanco>(CofreBanco.Unprotect(e.PixBancoSegredos))!;
            var cert = e.PixBancoCertificado is null ? null : JsonSerializer.Deserialize<CertificadoBanco>(CofreBanco.Unprotect(e.PixBancoCertificado));
            return new PixBancoCentral.Credenciais(e.PixBancoClientId, s.ClientSecret, s.ChaveApp, e.PixBancoTeste,
                cert is null ? null : Convert.FromBase64String(cert.Pfx), cert?.Senha);
        }
        catch (CryptographicException) { throw new PixException("Não foi possível ler as credenciais do banco: cadastre-as de novo em Configurações → Pix da loja."); }
    }

    // Endereço do aviso desta loja: o administrador cola no painel do Mercado Pago (Webhooks, evento "Order").
    // Sem ele funciona do mesmo jeito (o caixa pergunta a cada poucos segundos); com ele, o aviso chega na hora.
    public string? AvisoUrl(Empresa e) =>
        e.PixAvisoToken is null || e.PixAutomatico != ProvedoresPix.MercadoPago ? null : $"https://{config["Publico:HostClientes"] ?? "app.wnlabs.com.br"}/api/pix/mercadopago/aviso/{e.PixAvisoToken}";

    private static string Cortar(string v, int max) => v.Length <= max ? v : v[..max];
}

public class PixException(string mensagem) : Exception(mensagem);
