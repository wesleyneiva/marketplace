using System.Security.Cryptography;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Pix;

// Pix automático pelo Mercado Pago: cria a cobrança (QR único com o valor), acompanha até ser paga e recebe o aviso.
// REGRA DE SEGURANÇA: o Marcus só considera um Pix pago depois de PERGUNTAR ao Mercado Pago, com o token da loja,
// e conferir valor e referência. O aviso (webhook) só diz "olhe o pagamento X" — nunca é tomado como prova.
public class PixAutomaticoService(AppDbContext db, ContextoEmpresa contexto, MercadoPago mp, IDataProtectionProvider protecao,
    IConfiguration config, ILogger<PixAutomaticoService> log)
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30); // mínimo do Mercado Pago (API de Orders)
    private static readonly TimeSpan IntervaloConsulta = TimeSpan.FromSeconds(3);
    private IDataProtector Cofre => protecao.CreateProtector("Marcus.PixAutomatico.MercadoPago.v1");

    // ================================================================ configuração (Configurações → Pix da loja)

    public async Task<Empresa> LigarMercadoPagoAsync(string token, bool teste)
    {
        token = token.Trim();
        if (token.Length < 20 || token.Contains(' ')) throw new PixException("Cole o Access Token inteiro (começa com APP_USR- ou TEST-).");
        var conta = await mp.ContaAsync(token); // confere o token antes de guardar
        var e = await db.Empresas.FirstAsync(x => x.Id == contexto.EmpresaId);
        e.PixAutomatico = ProvedoresPix.MercadoPago;
        e.MercadoPagoToken = Cofre.Protect(token);
        e.MercadoPagoTokenFinal = token[^4..];
        e.MercadoPagoConta = Cortar(conta.Email is { Length: > 0 } em ? $"{conta.Nome} ({em})" : conta.Nome, 120);
        e.MercadoPagoTeste = teste || conta.Teste;
        e.PixAvisoToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        await db.SaveChangesAsync();
        return e;
    }

    public async Task<Empresa> DesligarAsync()
    {
        var e = await db.Empresas.FirstAsync(x => x.Id == contexto.EmpresaId);
        (e.PixAutomatico, e.MercadoPagoToken, e.MercadoPagoTokenFinal, e.MercadoPagoConta, e.MercadoPagoTeste) = (null, null, null, null, false);
        await db.SaveChangesAsync();
        return e;
    }

    // ================================================================ cobrança no caixa

    public async Task<CobrancaPix> CriarAsync(decimal valor, int? sessaoCaixaId, string? usuarioId)
    {
        valor = Math.Round(valor, 2);
        if (valor < 0.01m || valor > 999_999.99m) throw new PixException("Valor do Pix inválido.");
        var e = await db.Empresas.AsNoTracking().FirstAsync(x => x.Id == contexto.EmpresaId);
        var token = Token(e);
        if (sessaoCaixaId is int s && !await db.SessoesCaixa.AnyAsync(x => x.Id == s)) throw new PixException("Caixa não encontrado.");

        var c = new CobrancaPix { Valor = valor, ExpiraEm = DateTimeOffset.UtcNow + Validade, SessaoCaixaId = sessaoCaixaId, CriadaPorId = usuarioId };
        var pg = await mp.CriarPixAsync(token, valor, Cortar($"{e.Nome} · compra no caixa", 120), c.Referencia, Validade,
            e.EmailContato is { Length: > 3 } em && em.Contains('@') ? em : "cliente.pix@wnlabs.com.br", e.MercadoPagoTeste);
        if (string.IsNullOrEmpty(pg.CopiaECola)) throw new PixException("O Mercado Pago não devolveu o QR Code. Confira se a conta tem uma chave Pix cadastrada.");
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
                Aplicar(c, await mp.CancelarAsync(Token(e), c.IdExterno));
            }
            catch (MercadoPagoException ex) { log.LogWarning("Cancelar Pix {Id}: {Erro}", c.Id, ex.Message); }
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
            Aplicar(c, await mp.ConsultarAsync(Token(e), c.IdExterno));
        }
        catch (Exception ex) when (ex is MercadoPagoException or PixException)
        {
            log.LogWarning("Consultar Pix {Id}: {Erro}", c.Id, ex.Message); // tenta de novo na próxima consulta
        }
        if (c.Status == StatusCobrancaPix.Pendente && agora > c.ExpiraEm + TimeSpan.FromMinutes(2)) c.Status = StatusCobrancaPix.Expirada;
        await db.SaveChangesAsync();
    }

    // O que o Mercado Pago disse → situação da cobrança. "Aprovado" só vale com o MESMO valor e a MESMA referência.
    private void Aplicar(CobrancaPix c, MercadoPago.Pagamento pg)
    {
        c.StatusExterno = Cortar(pg.Status, 30);
        switch (pg.Status)
        {
            case "approved":
                if (pg.Referencia != c.Referencia.ToString() || Math.Abs(pg.Valor - c.Valor) >= 0.005m)
                {
                    log.LogError("Pix {Id}: Mercado Pago aprovou com dados diferentes (ref {Ref}, valor {Valor}).", c.Id, pg.Referencia, pg.Valor);
                    return;
                }
                c.Status = StatusCobrancaPix.Paga;
                c.PagaEm = (pg.AprovadoEm ?? DateTimeOffset.UtcNow).ToUniversalTime(); // o banco guarda em UTC (o Mercado Pago manda -03:00/-04:00)
                break;
            case "cancelled" or "rejected" or "refunded" or "charged_back":
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
            throw new PixException("O Pix automático não está ligado: o administrador liga em Configurações → Pix da loja.");
        try { return Cofre.Unprotect(e.MercadoPagoToken); }
        catch (CryptographicException) { throw new PixException("Não foi possível ler o token do Mercado Pago: cole-o de novo em Configurações → Pix da loja."); }
    }

    // Endereço do aviso desta loja: o administrador cola no painel do Mercado Pago (Webhooks, evento "Order").
    // Sem ele funciona do mesmo jeito (o caixa pergunta a cada poucos segundos); com ele, o aviso chega na hora.
    public string? AvisoUrl(Empresa e) =>
        e.PixAvisoToken is null ? null : $"https://{config["Publico:HostClientes"] ?? "app.wnlabs.com.br"}/api/pix/mercadopago/aviso/{e.PixAvisoToken}";

    private static string Cortar(string v, int max) => v.Length <= max ? v : v[..max];
}

public class PixException(string mensagem) : Exception(mensagem);
