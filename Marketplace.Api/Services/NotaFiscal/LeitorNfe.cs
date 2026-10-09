using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Marketplace.Api.Services.NotaFiscal;

// Lê o XML da NF-e (layout 4.00 da SEFAZ) e devolve só o que interessa para dar entrada no estoque.
// Aceita o arquivo "com protocolo" (<nfeProc>, o que o fornecedor manda) ou só a nota (<NFe>).
public static partial class LeitorNfe
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    public record Lote(string? Numero, decimal Quantidade, DateOnly? Fabricacao, DateOnly? Validade);

    public record Item(
        int NumeroItem,
        string CodigoFornecedor,     // cProd: o código do produto NO SISTEMA DO FORNECEDOR
        string? Ean,                 // cEAN: código de barras da unidade comercial (null se "SEM GTIN")
        string? EanTributavel,       // cEANTrib: código de barras da unidade tributável (muitas vezes a unidade de venda)
        string Descricao,
        string? Ncm,
        string? Cfop,
        string UnidadeComercial,     // uCom: como o fornecedor vendeu (CX, FD, UN, KG...)
        decimal QuantidadeComercial, // qCom
        decimal ValorUnitario,       // vUnCom
        string? UnidadeTributavel,   // uTrib
        decimal? QuantidadeTributavel, // qTrib (ex.: 2 CX → 24 UN)
        decimal ValorProdutos,       // vProd
        decimal Desconto,            // vDesc
        decimal Acrescimos,          // frete + seguro + outras despesas + IPI + ICMS-ST (entram no custo)
        List<Lote> Lotes)            // <rastro>: raro em mercado, mas quando vem traz a validade
    {
        // Quanto o item custou de verdade (o que sai do bolso): produtos − desconto + acréscimos.
        public decimal CustoTotal => ValorProdutos - Desconto + Acrescimos;
    }

    public record Nota(
        string Chave,                // 44 dígitos
        int Modelo,                  // 55 = NF-e; 65 = NFC-e (cupom)
        string Numero,
        string Serie,
        DateTimeOffset DataEmissao,
        string EmitenteCnpj,         // só números (CNPJ ou, raramente, CPF de produtor rural)
        string EmitenteNome,
        string? EmitenteFantasia,
        string? EmitenteUf,
        string? DestinatarioDocumento,
        string? DestinatarioNome,
        decimal ValorTotal,          // vNF
        bool Autorizada,             // protNFe com cStat 100 (ou 150)
        int TipoOperacao,            // tpNF: 0 = entrada, 1 = saída (do ponto de vista do emitente)
        List<Item> Itens);

    public class NotaInvalidaException(string mensagem) : Exception(mensagem);

    public static Nota Ler(Stream arquivo)
    {
        XDocument xml;
        try
        {
            // Sem DTD e sem entidades externas (proteção contra XML malicioso: XXE / "bilhão de risadas").
            var configuracao = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var leitor = XmlReader.Create(arquivo, configuracao);
            xml = XDocument.Load(leitor);
        }
        catch (XmlException)
        {
            throw new NotaInvalidaException("O arquivo não é um XML válido. Envie o XML da nota (não o PDF/DANFE).");
        }

        var infNFe = xml.Descendants(Ns + "infNFe").FirstOrDefault()
            ?? throw new NotaInvalidaException("Este XML não é de uma NF-e (não encontrei <infNFe>). Envie o XML da nota fiscal.");

        var chave = ((string?)infNFe.Attribute("Id") ?? "").Replace("NFe", "");
        if (!ChaveValida().IsMatch(chave))
            throw new NotaInvalidaException("A chave de acesso da nota está faltando ou errada.");

        var ide = Filho(infNFe, "ide");
        var emit = Filho(infNFe, "emit");
        var dest = infNFe.Element(Ns + "dest");
        var total = Filho(Filho(infNFe, "total"), "ICMSTot");

        var dataTexto = Texto(ide, "dhEmi") ?? Texto(ide, "dEmi")
            ?? throw new NotaInvalidaException("A nota não tem data de emissão.");
        var data = DateTimeOffset.TryParse(dataTexto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d : throw new NotaInvalidaException("Data de emissão inválida na nota.");

        var protocolo = xml.Descendants(Ns + "protNFe").Descendants(Ns + "cStat").FirstOrDefault()?.Value;

        var itens = infNFe.Elements(Ns + "det").Select(LerItem).ToList();
        if (itens.Count == 0)
            throw new NotaInvalidaException("A nota não tem nenhum item.");

        return new Nota(
            chave,
            int.TryParse(Texto(ide, "mod"), out var modelo) ? modelo : 55,
            Texto(ide, "nNF") ?? "?",
            Texto(ide, "serie") ?? "?",
            data,
            Texto(emit, "CNPJ") ?? Texto(emit, "CPF") ?? throw new NotaInvalidaException("A nota não tem o CNPJ do fornecedor."),
            Texto(emit, "xNome") ?? "Fornecedor sem nome",
            Texto(emit, "xFant"),
            emit.Element(Ns + "enderEmit") is { } ender ? Texto(ender, "UF") : null,
            dest is null ? null : Texto(dest, "CNPJ") ?? Texto(dest, "CPF"),
            dest is null ? null : Texto(dest, "xNome"),
            Decimal(total, "vNF"),
            protocolo is "100" or "150",
            int.TryParse(Texto(ide, "tpNF"), out var tipo) ? tipo : 1,
            itens);
    }

    private static Item LerItem(XElement det)
    {
        var prod = Filho(det, "prod");
        var imposto = det.Element(Ns + "imposto");

        // IPI e ICMS-ST são pagos pelo comprador junto com a nota: fazem parte do custo.
        var ipi = imposto?.Element(Ns + "IPI")?.Descendants(Ns + "vIPI").Sum(e => Numero(e.Value)) ?? 0;
        var st = imposto?.Element(Ns + "ICMS")?.Descendants(Ns + "vICMSST").Sum(e => Numero(e.Value)) ?? 0;
        var acrescimos = Decimal(prod, "vFrete") + Decimal(prod, "vSeg") + Decimal(prod, "vOutro") + ipi + st;

        var lotes = prod.Elements(Ns + "rastro").Select(r => new Lote(
            Texto(r, "nLote"), Decimal(r, "qLote"), Data(Texto(r, "dFab")), Data(Texto(r, "dVal")))).ToList();

        return new Item(
            int.TryParse((string?)det.Attribute("nItem"), out var n) ? n : 0,
            Texto(prod, "cProd") ?? "",
            CodigoDeBarras(Texto(prod, "cEAN")),
            CodigoDeBarras(Texto(prod, "cEANTrib")),
            Texto(prod, "xProd") ?? "(sem descrição)",
            Texto(prod, "NCM"),
            Texto(prod, "CFOP"),
            (Texto(prod, "uCom") ?? "UN").ToUpperInvariant(),
            Decimal(prod, "qCom"),
            Decimal(prod, "vUnCom"),
            Texto(prod, "uTrib")?.ToUpperInvariant(),
            Texto(prod, "qTrib") is string q ? Numero(q) : null,
            Decimal(prod, "vProd"),
            Decimal(prod, "vDesc"),
            acrescimos,
            lotes);
    }

    // "SEM GTIN", vazio ou com letras → sem código de barras. GTIN-14 com zero na frente ("0789…") = o EAN-13 de sempre.
    private static string? CodigoDeBarras(string? valor) =>
        valor is null || !CodigoBarrasValido().IsMatch(valor) ? null
        : valor.Length == 14 && valor[0] == '0' ? valor[1..] : valor;

    private static XElement Filho(XElement pai, string nome) =>
        pai.Element(Ns + nome) ?? throw new NotaInvalidaException($"XML incompleto: falta <{nome}>.");

    private static string? Texto(XElement pai, string nome) =>
        pai.Element(Ns + nome)?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static decimal Decimal(XElement pai, string nome) => Texto(pai, nome) is string v ? Numero(v) : 0;

    // A NF-e sempre usa ponto como separador decimal ("12.5000").
    private static decimal Numero(string valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static DateOnly? Data(string? valor) =>
        DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [GeneratedRegex(@"^\d{44}$")]
    private static partial Regex ChaveValida();

    [GeneratedRegex(@"^\d{8,14}$")]
    private static partial Regex CodigoBarrasValido();
}
