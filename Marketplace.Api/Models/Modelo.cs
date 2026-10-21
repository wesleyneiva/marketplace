namespace Marketplace.Api.Models;

// Modelo com GRADE de tamanho e cor (ex.: "Tênis Asics Gel-Resolution 10"). Cada combinação vendável — 42 Branco/Azul —
// é um Produto comum (ModeloId + Tamanho + Cor), com o seu código de barras, estoque e preço. Por isso venda, estoque,
// lotes, compras, NF-e e o caixa offline não mudam: continuam por produto. O modelo só agrupa (cadastro, matriz do
// estoque, escolha no caixa, relatórios). Ver docs/plano-ramo-e-grade.md (fase 2).
public class Modelo : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Nome { get; set; } = "";
    public string? Referencia { get; set; } // código do fabricante (ex.: "1041A375")
    public string? Marca { get; set; }

    public int CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
    public int? FornecedorId { get; set; }
    public Fornecedor? Fornecedor { get; set; }

    // Ordem das linhas e colunas da matriz (os tamanhos na ordem da grade: 38, 39, 40…; as cores na ordem cadastrada).
    // "Único" quando o modelo só varia em cor; "Única" quando só varia em tamanho.
    public string[] Tamanhos { get; set; } = [];
    public string[] Cores { get; set; } = [];

    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;

    public List<Produto> Variacoes { get; set; } = [];

    // Nome de cada variação no cupom e nas buscas: "Tênis Asics Gel-Resolution 10 42 Branco/Azul".
    public static string NomeDaVariacao(string modelo, string? tamanho, string? cor)
    {
        var partes = new List<string> { modelo.Trim() };
        if (!string.IsNullOrWhiteSpace(tamanho) && tamanho != TamanhoUnico) partes.Add(tamanho.Trim());
        if (!string.IsNullOrWhiteSpace(cor) && cor != CorUnica) partes.Add(cor.Trim());
        var nome = string.Join(' ', partes);
        return nome.Length <= 150 ? nome : nome[..150];
    }

    public const string TamanhoUnico = "Único";
    public const string CorUnica = "Única";
}
