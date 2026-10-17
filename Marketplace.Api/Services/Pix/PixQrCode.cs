using System.Globalization;
using System.Text;
using QRCoder;

namespace Marketplace.Api.Services.Pix;

// Pix com QR Code ESTÁTICO com o valor da venda (padrão "BR Code" do Banco Central, sem empresa intermediária):
// o dinheiro cai direto na conta da chave Pix da loja. O caixa confere no celular da loja antes de confirmar a venda,
// e a conferência fica pelo extrato do banco (Financeiro → Extrato do banco).
// Formato EMV (Manual de Padrões para Iniciação do Pix, BCB): campos "ID + tamanho (2 dígitos) + valor" e, no fim, o CRC16.
public static class PixQrCode
{
    public const int MaxNome = 25;    // campo 59 (nome do recebedor)
    public const int MaxCidade = 15;  // campo 60 (cidade)
    public const int MaxTxId = 25;    // campo 62/05 (identificador; no QR estático, só letras e números)

    public static readonly string[] Tipos = ["Cpf", "Cnpj", "Celular", "Email", "Aleatoria"];

    // Confere e põe a chave no jeito que o Banco Central pede (CPF/CNPJ só números, celular +55DDD…, e-mail minúsculo).
    public static (string? Chave, string? Erro) NormalizarChave(string? tipo, string? texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return (null, "Informe a chave Pix.");
        switch (tipo)
        {
            case "Cpf":
                var cpf = Documentos.SoDigitos(t);
                return cpf.Length == 11 && Documentos.CpfValido(cpf) ? (cpf, null) : (null, "CPF inválido: confira os números.");
            case "Cnpj":
                var cnpj = Documentos.SoDigitos(t);
                return cnpj.Length == 14 && Documentos.CnpjValido(cnpj) ? (cnpj, null) : (null, "CNPJ inválido: confira os números.");
            case "Celular":
                var d = Documentos.SoDigitos(t);
                if (d.StartsWith("55") && d.Length is 12 or 13) d = d[2..];
                return d.Length is 10 or 11 && d[0] != '0' ? ("+55" + d, null) : (null, "Celular: informe o DDD e o número (ex.: 51 99999-9999).");
            case "Email":
                var e = t.ToLowerInvariant();
                return e.Length <= 77 && e.IndexOf('@') > 0 && e.IndexOf('@') < e.Length - 3 && !e.Contains(' ') ? (e, null) : (null, "E-mail inválido.");
            case "Aleatoria":
                return Guid.TryParse(t, out var g) ? (g.ToString("D"), null) : (null, "Chave aleatória: são 32 letras e números com tracinhos (ex.: 123e4567-e89b-12d3-a456-426614174000).");
            default:
                return (null, "Escolha o tipo da chave: CPF, CNPJ, celular, e-mail ou aleatória.");
        }
    }

    // O texto do "Pix copia e cola" (é ele que vai dentro do QR Code).
    // valor nulo = QR "fixo" (o cliente digita o valor no app do banco): o caixa guarda para usar sem internet.
    public static string Payload(string chave, string nome, string? cidade, decimal? valor, string? txId)
    {
        var conta = Campo("00", "br.gov.bcb.pix") + Campo("01", chave);
        var tx = SoLetrasENumeros(txId ?? "");
        var sb = new StringBuilder()
            .Append(Campo("00", "01"))                                     // versão do formato
            .Append(Campo("26", conta))                                    // quem recebe (a chave Pix)
            .Append(Campo("52", "0000"))                                   // categoria do comércio (não informada)
            .Append(Campo("53", "986"))                                    // moeda: real
            .Append(valor is decimal v ? Campo("54", v.ToString("0.00", CultureInfo.InvariantCulture)) : "")
            .Append(Campo("58", "BR"))
            .Append(Campo("59", Texto(nome, MaxNome, "RECEBEDOR")))
            .Append(Campo("60", Texto(cidade, MaxCidade, "BRASIL")))
            .Append(Campo("62", Campo("05", tx.Length == 0 ? "***" : tx[..Math.Min(tx.Length, MaxTxId)])))
            .Append("6304");                                               // CRC16 (calculado sobre tudo, incluindo o "6304")
        return sb.Append(Crc16(sb.ToString()).ToString("X4")).ToString();
    }

    // O QR Code desenhado (SVG que cresce sem perder nitidez; fundo branco para o celular ler fácil, até no modo escuro).
    public static string Svg(string payload)
    {
        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(dados).GetGraphic(8, "#000000", "#ffffff", drawQuietZones: true,
            sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    // "Mercado do Zé Ltda." → "Mercado do Ze Ltda" (o QR só aceita letras sem acento).
    public static string Texto(string? v, int max, string padrao)
    {
        var semAcento = new StringBuilder();
        foreach (var c in (v ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= ' ' and <= '~') semAcento.Append(c);
        }
        var r = string.Join(' ', semAcento.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (r.Length == 0) r = padrao;
        return r.Length <= max ? r : r[..max].TrimEnd();
    }

    private static string Campo(string id, string valor) => id + valor.Length.ToString("00") + valor;

    private static string SoLetrasENumeros(string v) => new(v.Where(char.IsAsciiLetterOrDigit).ToArray());

    // CRC16-CCITT (polinômio 0x1021, começa em 0xFFFF), como pede o padrão.
    public static ushort Crc16(string texto)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(texto))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}
