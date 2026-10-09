namespace Marketplace.Api.Models;

// Lote = um grupo de unidades do mesmo produto com a MESMA data de validade.
// Ex.: chegaram 24 iogurtes que vencem dia 15 e, depois, 24 que vencem dia 22 → 2 lotes.
// Só existe para produtos perecíveis (ControlaValidade = true).
public class LoteValidade : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public DateOnly DataValidade { get; set; }

    public decimal QuantidadeInicial { get; set; }
    public decimal QuantidadeAtual { get; set; }

    public DateTimeOffset DataEntrada { get; set; } = DateTimeOffset.UtcNow;
}
