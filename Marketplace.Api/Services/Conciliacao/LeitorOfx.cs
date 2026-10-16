using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marketplace.Api.Services.Conciliacao;

// Lê o extrato bancário no formato OFX ("Money" / "Microsoft Money" no internet banking).
// Aceita os dois jeitos que os bancos geram:
//   - OFX 1.x (SGML): as tags NÃO fecham — <TRNAMT>-45.90 e a próxima tag na linha seguinte (o mais comum no Brasil);
//   - OFX 2.x (XML): <TRNAMT>-45.90</TRNAMT>.
// Bancos brasileiros às vezes mandam valor com vírgula ("-45,90") e o arquivo em Latin-1 (CHARSET:1252): tudo tratado.
public static partial class LeitorOfx
{
    public const int TamanhoMaximo = 5 * 1024 * 1024;

    public sealed record Transacao(DateOnly Data, decimal Valor, string? FitId, string Descricao, string? Documento, string? Tipo);

    public sealed record Extrato(string? Banco, string? Agencia, string? Conta, DateOnly? De, DateOnly? Ate,
                                 decimal? Saldo, DateOnly? SaldoEm, List<Transacao> Transacoes);

    public static Extrato Ler(byte[] bytes)
    {
        var texto = Decodificar(bytes);
        if (!texto.Contains("<OFX", StringComparison.OrdinalIgnoreCase))
            throw new ExtratoInvalidoException("Este arquivo não é um extrato OFX. No internet banking, exporte o extrato no formato OFX (às vezes chamado de \"Money\").");

        var transacoes = new List<Transacao>();
        foreach (Match m in BlocoTransacao().Matches(texto))
        {
            var bloco = m.Groups[1].Value;
            var data = Data(Campo(bloco, "DTPOSTED"));
            var valor = Valor(Campo(bloco, "TRNAMT"));
            if (data is null || valor is null || valor == 0) continue; // linha informativa (saldo do dia etc.)
            var memo = Limpar(Campo(bloco, "MEMO"));
            var nome = Limpar(Campo(bloco, "NAME"));
            // Cada banco põe o texto num lugar: junta NAME e MEMO sem repetir.
            var descricao = string.IsNullOrEmpty(nome) ? memo
                : string.IsNullOrEmpty(memo) || memo.Contains(nome, StringComparison.OrdinalIgnoreCase) ? (string.IsNullOrEmpty(memo) ? nome : memo)
                : nome.Contains(memo, StringComparison.OrdinalIgnoreCase) ? nome : $"{nome} · {memo}";
            if (string.IsNullOrWhiteSpace(descricao)) descricao = Campo(bloco, "TRNTYPE") ?? "Lançamento do banco";
            var documento = Limpar(Campo(bloco, "CHECKNUM")) is { Length: > 0 } ch ? ch : Limpar(Campo(bloco, "REFNUM"));
            transacoes.Add(new Transacao(data.Value, valor.Value, Limpar(Campo(bloco, "FITID")) is { Length: > 0 } f ? f : null,
                Cortar(descricao, 200), string.IsNullOrEmpty(documento) ? null : Cortar(documento, 60), Limpar(Campo(bloco, "TRNTYPE"))));
        }
        if (transacoes.Count == 0)
            throw new ExtratoInvalidoException("O arquivo OFX não tem lançamentos. Confira o período escolhido no internet banking.");

        // Saldo informado pelo banco (LEDGERBAL = saldo contábil no fim do período).
        decimal? saldo = null; DateOnly? saldoEm = null;
        if (SaldoContabil().Match(texto) is { Success: true } sm)
        {
            saldo = Valor(Campo(sm.Groups[1].Value, "BALAMT"));
            saldoEm = Data(Campo(sm.Groups[1].Value, "DTASOF"));
        }
        var de = Data(Campo(texto, "DTSTART")) ?? transacoes.Min(t => t.Data);
        var ate = Data(Campo(texto, "DTEND")) ?? transacoes.Max(t => t.Data);
        if (ate < transacoes.Max(t => t.Data)) ate = transacoes.Max(t => t.Data);
        if (de > transacoes.Min(t => t.Data)) de = transacoes.Min(t => t.Data);

        return new Extrato(Limpar(Campo(texto, "BANKID")), Limpar(Campo(texto, "BRANCHID")), Limpar(Campo(texto, "ACCTID")),
            de, ate, saldo, saldoEm, transacoes);
    }

    // Nome do banco pelo código (COMPE) que vem no OFX.
    public static string? NomeBanco(string? codigo) => (codigo ?? "").TrimStart('0') switch
    {
        "1" => "Banco do Brasil", "104" => "Caixa Econômica Federal", "237" => "Bradesco", "341" => "Itaú", "33" => "Santander",
        "260" => "Nubank", "77" => "Inter", "336" => "C6 Bank", "756" => "Sicoob", "748" => "Sicredi", "41" => "Banrisul",
        "290" => "PagBank", "323" => "Mercado Pago", "197" => "Stone", "380" => "PicPay", "422" => "Safra", "70" => "BRB",
        "655" => "Votorantim", "212" => "Banco Original", "403" => "Cora", "208" => "BTG Pactual",
        _ => null,
    };

    // ------------------------------------------------------------------ detalhes do formato

    private static string Decodificar(byte[] bytes)
    {
        // O cabeçalho diz o jeito (CHARSET:1252 ou encoding="UTF-8"); na dúvida: UTF-8 se for válido, senão Latin-1.
        var cabeca = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 600));
        var utf8 = cabeca.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) || cabeca.Contains("CHARSET:UTF", StringComparison.OrdinalIgnoreCase);
        if (utf8 || !cabeca.Contains("1252"))
        {
            try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
            catch (DecoderFallbackException) { /* não era UTF-8 de verdade */ }
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetString(bytes);
    }

    // Valor de uma tag: <TAG>valor  (até a próxima tag ou o fim da linha; funciona com e sem a tag de fechamento).
    private static string? Campo(string texto, string tag)
    {
        var m = Regex.Match(texto, $@"<{tag}>\s*([^<\r\n]*)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // 20261015, 20261015120000, 20261015120000[-3:BRT] → 15/10/2026
    private static DateOnly? Data(string? v)
    {
        if (v is null || v.Length < 8) return null;
        return DateOnly.TryParseExact(v[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // "-45.90", "-45,90", "1.234,56", "+120.00" → decimal
    private static decimal? Valor(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim().Replace(" ", "").Replace("+", "");
        var ponto = v.LastIndexOf('.'); var virgula = v.LastIndexOf(',');
        if (ponto >= 0 && virgula >= 0)
            v = virgula > ponto ? v.Replace(".", "").Replace(',', '.') : v.Replace(",", "");
        else if (virgula >= 0) v = v.Replace(',', '.');
        return decimal.TryParse(v, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var d)
            ? Math.Round(d, 2) : null;
    }

    private static string Limpar(string? v) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(v ?? "", @"\s+", " ")).Trim();

    private static string Cortar(string v, int max) => v.Length <= max ? v : v[..max];

    // Um bloco <STMTTRN> … (fecha com </STMTTRN> no XML; no SGML às vezes só começa o próximo).
    [GeneratedRegex(@"<STMTTRN>(.*?)(?=</STMTTRN>|<STMTTRN>|</BANKTRANLIST>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlocoTransacao();

    [GeneratedRegex(@"<LEDGERBAL>(.*?)(?=</LEDGERBAL>|<AVAILBAL>|</STMTRS>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SaldoContabil();
}

public class ExtratoInvalidoException(string mensagem) : Exception(mensagem);
