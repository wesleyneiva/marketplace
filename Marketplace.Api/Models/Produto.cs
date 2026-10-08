namespace Marketplace.Api.Models;

// Produto vendido no mercado.
public class Produto
{
    public int Id { get; set; }

    // EAN-13 (código de barras). Opcional: pão e frutas a granel normalmente não têm.
    public string? CodigoBarras { get; set; }

    public string Nome { get; set; } = "";

    public int CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }

    // UN (unidade), KG, L, PCT, CX, DZ
    public string Unidade { get; set; } = Unidades.Unidade;

    public decimal PrecoCusto { get; set; }
    public decimal PrecoVenda { get; set; }

    // decimal porque produto a granel tem estoque "quebrado" (ex.: 12,350 kg).
    // Só muda por movimentação de estoque (entrada, venda, perda) — não pelo cadastro.
    public decimal EstoqueAtual { get; set; }
    public decimal EstoqueMinimo { get; set; }

    // Perecível? (vai alimentar o alerta de "validade chegando")
    public bool ControlaValidade { get; set; }

    // Produto nunca é apagado (as vendas antigas apontam para ele): só desativado.
    public bool Ativo { get; set; } = true;

    // Lotes de validade (só perecíveis).
    public List<LoteValidade> Lotes { get; set; } = [];

    // Controle de concorrência: se duas pessoas mexerem no estoque do mesmo produto ao mesmo
    // tempo, a segunda recebe um erro em vez de sobrescrever a primeira. (No PostgreSQL, usa a
    // coluna interna "xmin", que muda a cada alteração da linha.)
    public uint Versao { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public static class Unidades
{
    public const string Unidade = "UN";
    public const string Quilo = "KG";
    public const string Litro = "L";
    public const string Pacote = "PCT";
    public const string Caixa = "CX";
    public const string Duzia = "DZ";

    public static readonly string[] Todas = [Unidade, Quilo, Litro, Pacote, Caixa, Duzia];
}
