using ClosedXML.Excel;
using Marketplace.Api.Data;
using Marketplace.Api.Models;

namespace Marketplace.Api.Services;

// Importação de produtos por planilha (para o cliente novo não cadastrar 1.000 produtos um por um).
// Passo 1: o MODELO que o cliente baixa e preenche. A leitura/prévia usa as mesmas colunas (Colunas).
public partial class ImportacaoProdutosService(AppDbContext db)
{
    // Uma coluna da planilha: título (como aparece no Excel), se é obrigatória e a explicação da aba "Instruções".
    public record Coluna(string Titulo, bool Obrigatoria, string Explicacao, string Exemplo);

    // A ORDEM aqui é a ordem das colunas no Excel (A, B, C...).
    public static readonly Coluna[] Colunas =
    [
        new("Código de barras", false, "Os números do código de barras (EAN), de 8 a 14 dígitos. Pode ficar vazio (ex.: pão, frutas). " +
            "Se o produto já existir com este código, a importação ATUALIZA o produto em vez de criar outro.", "7891234567895"),
        new("Nome", true, "Nome do produto como aparece no cupom (2 a 150 letras).", "Arroz Branco Tipo 1 5kg"),
        new("Categoria", true, "Escolha da lista ou escreva uma nova: categoria que não existe é criada na importação.", "Mercearia"),
        new("Unidade", true, $"Como o produto é vendido: {string.Join(", ", Unidades.Todas)} (UN = unidade, KG = quilo, L = litro, " +
            "PCT = pacote, CX = caixa, DZ = dúzia). KG pede o peso na hora da venda.", "UN"),
        new("Preço de custo", false, "Quanto você paga ao fornecedor, em reais. Vazio = 0.", "22,90"),
        new("Preço de venda", true, "Preço na prateleira, em reais (maior que zero).", "29,90"),
        new("Estoque mínimo", false, "Abaixo disso o produto aparece em \"estoque baixo\" e na sugestão de compra. Vazio = 0.", "10"),
        new("Controla validade", false, "Sim ou Não. \"Sim\" = o sistema guarda lotes com data de validade e vende primeiro o que vence " +
            "antes. Vazio = Não.", "Sim"),
        new("Estoque inicial", false, "Quantidade que você TEM HOJE na loja (opcional). Entra como \"inventário inicial\" no histórico. " +
            "Só vale para produto novo; para corrigir o estoque de um produto que já existe, use a tela de Estoque.", "40"),
        new("Validade", false, "Data de validade do estoque inicial (dd/mm/aaaa). Obrigatória quando \"Controla validade\" = Sim e há " +
            "estoque inicial.", "20/03/2027"),
    ];

    public const string AbaProdutos = "Produtos";
    private static readonly XLColor Verde = XLColor.FromHtml("#15803d");
    private static readonly XLColor VerdeClaro = XLColor.FromHtml("#dcfce7");

    // Gera o .xlsx com 4 abas: Produtos (para preencher), Exemplo, Instruções e Listas (escondida, alimenta os menus).
    public byte[] GerarModelo(IReadOnlyList<string> categorias)
    {
        using var planilha = new XLWorkbook();

        var produtos = planilha.AddWorksheet(AbaProdutos);
        var exemplo = planilha.AddWorksheet("Exemplo");
        var instrucoes = planilha.AddWorksheet("Instruções");
        var listas = planilha.AddWorksheet("Listas");

        MontarListas(listas, categorias);
        MontarAbaDeProdutos(produtos, linhasComMenus: 3000);
        MontarAbaDeProdutos(exemplo, linhasComMenus: 0);
        PreencherExemplo(exemplo);
        MontarInstrucoes(instrucoes);

        produtos.SetTabActive();
        using var memoria = new MemoryStream();
        planilha.SaveAs(memoria);
        return memoria.ToArray();
    }

    // Cabeçalho verde, colunas com o formato certo e os menus (Categoria, Unidade, Sim/Não).
    private static void MontarAbaDeProdutos(IXLWorksheet aba, int linhasComMenus)
    {
        for (var i = 0; i < Colunas.Length; i++)
        {
            var celula = aba.Cell(1, i + 1);
            celula.Value = Colunas[i].Titulo + (Colunas[i].Obrigatoria ? " *" : "");
            celula.GetComment().AddText(Colunas[i].Explicacao);
        }
        var cabecalho = aba.Range(1, 1, 1, Colunas.Length);
        cabecalho.Style.Font.Bold = true;
        cabecalho.Style.Font.FontColor = XLColor.White;
        cabecalho.Style.Fill.BackgroundColor = Verde;
        cabecalho.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        aba.Row(1).Height = 22;
        aba.SheetView.FreezeRows(1);

        // Formatos: código de barras como TEXTO (senão o Excel mostra 7,89123E+12 e perde dígitos),
        // dinheiro com 2 casas, quantidade com até 3 (gramas no KG) e data dd/mm/aaaa.
        aba.Column(1).Style.NumberFormat.Format = "@";
        aba.Column(5).Style.NumberFormat.Format = "#,##0.00";
        aba.Column(6).Style.NumberFormat.Format = "#,##0.00";
        aba.Column(7).Style.NumberFormat.Format = "#,##0.###";
        aba.Column(9).Style.NumberFormat.Format = "#,##0.###";
        aba.Column(10).Style.NumberFormat.Format = "dd/mm/yyyy";

        int[] larguras = [18, 42, 20, 10, 15, 15, 15, 18, 15, 13];
        for (var i = 0; i < larguras.Length; i++)
            aba.Column(i + 1).Width = larguras[i];

        if (linhasComMenus == 0) return;
        var ultima = linhasComMenus + 1;

        // Categoria: sugere a lista, mas aceita uma nova (só avisa).
        var categoria = aba.Range(2, 3, ultima, 3).CreateDataValidation();
        categoria.List("CategoriasLista", true);
        categoria.ErrorStyle = XLErrorStyle.Information;
        categoria.ErrorTitle = "Categoria nova";
        categoria.ErrorMessage = "Esta categoria ainda não existe: ela será criada na importação.";

        // Unidade e Controla validade: só os valores da lista.
        var unidade = aba.Range(2, 4, ultima, 4).CreateDataValidation();
        unidade.List("UnidadesLista", true);
        unidade.ErrorTitle = "Unidade inválida";
        unidade.ErrorMessage = $"Use uma destas: {string.Join(", ", Unidades.Todas)}.";

        var validade = aba.Range(2, 8, ultima, 8).CreateDataValidation();
        validade.List("\"Sim,Não\"", true);
        validade.ErrorTitle = "Sim ou Não";
        validade.ErrorMessage = "Escolha Sim ou Não.";
    }

    private static void MontarListas(IXLWorksheet aba, IReadOnlyList<string> categorias)
    {
        aba.Cell(1, 1).Value = "Categorias";
        aba.Cell(1, 2).Value = "Unidades";
        for (var i = 0; i < categorias.Count; i++) aba.Cell(i + 2, 1).Value = categorias[i];
        for (var i = 0; i < Unidades.Todas.Length; i++) aba.Cell(i + 2, 2).Value = Unidades.Todas[i];

        var planilha = aba.Workbook;
        planilha.DefinedNames.Add("CategoriasLista", aba.Range(2, 1, Math.Max(categorias.Count, 1) + 1, 1));
        planilha.DefinedNames.Add("UnidadesLista", aba.Range(2, 2, Unidades.Todas.Length + 1, 2));
        aba.Hide();
    }

    private static void PreencherExemplo(IXLWorksheet aba)
    {
        object?[][] linhas =
        [
            ["7891234567895", "Arroz Branco Tipo 1 5kg", "Mercearia", "UN", 22.90m, 29.90m, 10m, "Não", 40m, null],
            ["7899876543210", "Iogurte Natural 170g", "Frios e Laticínios", "UN", 2.10m, 3.49m, 12m, "Sim", 24m, new DateTime(2027, 3, 20)],
            [null, "Banana Prata", "Hortifrúti", "KG", 3.20m, 6.99m, 5m, "Sim", 18.5m, new DateTime(2026, 10, 16)],
            [null, "Pão Francês", "Padaria", "KG", 7.50m, 15.90m, 0m, "Não", null, null],
        ];
        for (var l = 0; l < linhas.Length; l++)
            for (var c = 0; c < linhas[l].Length; c++)
                aba.Cell(l + 2, c + 1).Value = linhas[l][c] switch
                {
                    null => Blank.Value,
                    string s => s,
                    decimal d => d,
                    DateTime d => d,
                    _ => throw new InvalidOperationException(),
                };
        aba.Range(2, 1, linhas.Length + 1, Colunas.Length).Style.Fill.BackgroundColor = VerdeClaro;
        aba.Cell(linhas.Length + 3, 1).Value =
            "Esta aba é só um exemplo e NÃO é importada. Preencha os seus produtos na aba \"Produtos\".";
        aba.Cell(linhas.Length + 3, 1).Style.Font.Italic = true;
    }

    private static void MontarInstrucoes(IXLWorksheet aba)
    {
        string[] passos =
        [
            "Como importar os seus produtos",
            "",
            "1. Preencha a aba \"Produtos\": uma linha por produto, a partir da linha 2. Não mude os títulos da linha 1.",
            "2. Colunas com * são obrigatórias. Veja a aba \"Exemplo\" e passe o mouse nos títulos para ver a explicação.",
            "3. Salve o arquivo (.xlsx) e envie na tela Produtos → Importar planilha.",
            "4. O sistema mostra uma PRÉVIA com os erros de cada linha antes de gravar qualquer coisa. Nada é gravado até você confirmar.",
            "5. Pode importar de novo quando quiser: produtos com o mesmo código de barras são ATUALIZADOS (ex.: tabela de preços nova).",
            "",
            "Dica: se você tem os produtos em outro sistema, exporte para Excel e copie as colunas para a aba \"Produtos\".",
            "Também aceitamos .csv (separado por ponto e vírgula, como o Excel brasileiro salva).",
        ];
        for (var i = 0; i < passos.Length; i++) aba.Cell(i + 1, 1).Value = passos[i];
        aba.Cell(1, 1).Style.Font.Bold = true;
        aba.Cell(1, 1).Style.Font.FontSize = 14;
        aba.Cell(1, 1).Style.Font.FontColor = Verde;

        var inicio = passos.Length + 2;
        aba.Cell(inicio, 1).Value = "Coluna";
        aba.Cell(inicio, 2).Value = "Obrigatória?";
        aba.Cell(inicio, 3).Value = "O que colocar";
        aba.Cell(inicio, 4).Value = "Exemplo";
        var cabecalho = aba.Range(inicio, 1, inicio, 4);
        cabecalho.Style.Font.Bold = true;
        cabecalho.Style.Font.FontColor = XLColor.White;
        cabecalho.Style.Fill.BackgroundColor = Verde;

        for (var i = 0; i < Colunas.Length; i++)
        {
            var linha = inicio + 1 + i;
            aba.Cell(linha, 1).Value = Colunas[i].Titulo;
            aba.Cell(linha, 2).Value = Colunas[i].Obrigatoria ? "Sim" : "Não";
            aba.Cell(linha, 3).Value = Colunas[i].Explicacao;
            aba.Cell(linha, 4).SetValue(Colunas[i].Exemplo);
        }
        aba.Column(1).Width = 20;
        aba.Column(2).Width = 13;
        aba.Column(3).Width = 90;
        aba.Column(4).Width = 26;
        aba.Range(inicio + 1, 3, inicio + Colunas.Length, 3).Style.Alignment.WrapText = true;
        aba.Range(inicio + 1, 1, inicio + Colunas.Length, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }
}
