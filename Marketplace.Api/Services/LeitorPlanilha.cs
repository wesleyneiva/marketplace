using System.Text;
using ClosedXML.Excel;

namespace Marketplace.Api.Services;

// Lê uma planilha (.xlsx ou .csv) e devolve o cabeçalho + as linhas, sem interpretar nada ainda.
// Cada célula guarda o que veio: texto, número ou data (o .xlsx diz o tipo; no .csv tudo é texto).
public static class LeitorPlanilha
{
    public record Celula(string? Texto, double? Numero = null, DateTime? Data = null)
    {
        public static readonly Celula Vazia = new((string?)null);
        public bool EstaVazia => string.IsNullOrWhiteSpace(Texto) && Numero is null && Data is null;
    }

    // NumeroDaLinha = a linha como o cliente vê no Excel (o cabeçalho é a 1).
    public record Linha(int NumeroDaLinha, Celula[] Celulas);
    public record Resultado(string[] Cabecalho, List<Linha> Linhas);

    public class PlanilhaInvalidaException(string mensagem) : Exception(mensagem);

    public static Resultado Ler(Stream arquivo, string nomeDoArquivo, string abaPreferida, int maxLinhas)
    {
        var extensao = Path.GetExtension(nomeDoArquivo).ToLowerInvariant();
        return extensao switch
        {
            ".xlsx" => LerXlsx(arquivo, abaPreferida, maxLinhas),
            ".csv" or ".txt" => LerCsv(arquivo, maxLinhas),
            ".xls" => throw new PlanilhaInvalidaException(
                "Arquivo .xls (Excel antigo) não é aceito: abra no Excel e use \"Salvar como\" → Pasta de Trabalho do Excel (.xlsx)."),
            _ => throw new PlanilhaInvalidaException("Envie uma planilha .xlsx (Excel) ou .csv."),
        };
    }

    private static Resultado LerXlsx(Stream arquivo, string abaPreferida, int maxLinhas)
    {
        XLWorkbook planilha;
        try { planilha = new XLWorkbook(arquivo); }
        catch (Exception) { throw new PlanilhaInvalidaException("Não consegui abrir o arquivo. Ele é mesmo uma planilha do Excel (.xlsx)?"); }

        using (planilha)
        {
            // A aba "Produtos" do modelo; se o cliente renomeou ou usou outra planilha, a primeira aba visível.
            var aba = planilha.Worksheets.FirstOrDefault(a => a.Name.Equals(abaPreferida, StringComparison.OrdinalIgnoreCase))
                      ?? planilha.Worksheets.FirstOrDefault(a => a.Visibility == XLWorksheetVisibility.Visible)
                      ?? throw new PlanilhaInvalidaException("A planilha não tem nenhuma aba.");

            var usada = aba.RangeUsed();
            if (usada is null) return new([], []);

            var ultimaColuna = usada.LastColumn().ColumnNumber();
            var ultimaLinha = usada.LastRow().RowNumber();
            if (ultimaLinha - 1 > maxLinhas)
                throw new PlanilhaInvalidaException($"A planilha tem mais de {maxLinhas} linhas. Divida em arquivos menores.");

            var cabecalho = Enumerable.Range(1, ultimaColuna).Select(c => aba.Cell(1, c).GetFormattedString().Trim()).ToArray();
            var linhas = new List<Linha>();
            for (var l = 2; l <= ultimaLinha; l++)
            {
                var celulas = Enumerable.Range(1, ultimaColuna).Select(c => Converter(aba.Cell(l, c))).ToArray();
                if (celulas.All(c => c.EstaVazia)) continue;
                linhas.Add(new(l, celulas));
            }
            return new(cabecalho, linhas);
        }
    }

    private static Celula Converter(IXLCell celula)
    {
        var valor = celula.Value;
        return valor.Type switch
        {
            XLDataType.Blank => Celula.Vazia,
            XLDataType.Number => new(celula.GetFormattedString(), valor.GetNumber()),
            XLDataType.DateTime => new(celula.GetFormattedString(), Data: valor.GetDateTime()),
            XLDataType.Boolean => new(valor.GetBoolean() ? "Sim" : "Não"),
            XLDataType.Error => new("#ERRO"),
            _ => new(valor.ToString().Trim()),
        };
    }

    // CSV do jeito que o Excel brasileiro salva: separado por ";" e em Windows-1252 (ANSI).
    // Também aceita "," ou TAB e UTF-8 (com ou sem BOM).
    private static Resultado LerCsv(Stream arquivo, int maxLinhas)
    {
        using var memoria = new MemoryStream();
        arquivo.CopyTo(memoria);
        var texto = Decodificar(memoria.ToArray());

        var registros = DividirCsv(texto);
        if (registros.Count == 0) return new([], []);
        if (registros.Count - 1 > maxLinhas)
            throw new PlanilhaInvalidaException($"O arquivo tem mais de {maxLinhas} linhas. Divida em arquivos menores.");

        var cabecalho = registros[0].Select(c => c.Trim()).ToArray();
        var linhas = new List<Linha>();
        for (var i = 1; i < registros.Count; i++)
        {
            var celulas = registros[i].Select(c => string.IsNullOrWhiteSpace(c) ? Celula.Vazia : new Celula(c.Trim())).ToArray();
            if (celulas.All(c => c.EstaVazia)) continue;
            linhas.Add(new(i + 1, celulas));
        }
        return new(cabecalho, linhas);
    }

    private static string Decodificar(byte[] bytes)
    {
        try
        {
            // UTF-8 "rigoroso": se aparecer um byte que não é UTF-8 válido, é ANSI.
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    // Divide o CSV respeitando aspas ("Arroz; tipo 1" fica numa célula só; "" vira ").
    private static List<string[]> DividirCsv(string texto)
    {
        var primeiraLinha = texto.Split('\n', 2)[0];
        var separador = new[] { ';', '\t', ',' }.MaxBy(s => primeiraLinha.Count(c => c == s));

        var registros = new List<string[]>();
        var atual = new List<string>();
        var celula = new StringBuilder();
        var entreAspas = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreAspas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { celula.Append('"'); i++; }
                else if (c == '"') entreAspas = false;
                else celula.Append(c);
            }
            else if (c == '"') entreAspas = true;
            else if (c == separador) { atual.Add(celula.ToString()); celula.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                atual.Add(celula.ToString()); celula.Clear();
                registros.Add([.. atual]); atual.Clear();
            }
            else celula.Append(c);
        }
        if (celula.Length > 0 || atual.Count > 0) { atual.Add(celula.ToString()); registros.Add([.. atual]); }
        return registros;
    }
}
