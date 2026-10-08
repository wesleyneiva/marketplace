namespace Marketplace.Api.Models;

// Cada mudança no estoque vira uma linha aqui: é o "extrato" do produto.
// Nunca se edita nem se apaga uma movimentação — erros se corrigem com um ajuste.
public class MovimentacaoEstoque
{
    public long Id { get; set; }

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public TipoMovimentacao Tipo { get; set; }

    // Com sinal: positivo entra (+24), negativo sai (-3). Assim, somar tudo dá o estoque.
    public decimal Quantidade { get; set; }

    // Fotografia do estoque antes e depois (facilita auditoria: "estava 10, ficou 7").
    public decimal EstoqueAnterior { get; set; }
    public decimal EstoquePosterior { get; set; }

    // Na entrada: quanto custou cada unidade nesta compra.
    public decimal? CustoUnitario { get; set; }

    // Na perda: Vencido, Avariado, Furto/Extravio, Consumo interno, Outro.
    public string? Motivo { get; set; }
    public string? Observacao { get; set; }

    // Lote de validade envolvido (perecíveis).
    public int? LoteId { get; set; }
    public LoteValidade? Lote { get; set; }

    // Quem fez. Nulo = o próprio sistema (ex.: inventário inicial do catálogo fictício).
    public string? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public DateTimeOffset DataHora { get; set; } = DateTimeOffset.UtcNow;
}

public enum TipoMovimentacao
{
    Entrada,      // chegou mercadoria do fornecedor
    Venda,        // saiu pelo caixa (vai ser usado pelo PDV)
    Perda,        // venceu, quebrou, sumiu...
    Ajuste,       // contagem de inventário: o sistema dizia X, a prateleira tem Y
    Inventario,   // estoque inicial (quando o sistema começou a ser usado)
}
