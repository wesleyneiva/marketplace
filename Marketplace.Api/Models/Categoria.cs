namespace Marketplace.Api.Models;

// Seção do mercado (Mercearia, Bebidas, Hortifrúti...).
public class Categoria : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string Nome { get; set; } = "";
    public bool Ativa { get; set; } = true;
    public decimal? ComissaoPercentual { get; set; } // comissão própria da categoria (vale no lugar do % do vendedor)

    // Navegação: "os produtos desta categoria" (o EF preenche quando pedimos).
    public List<Produto> Produtos { get; set; } = [];
}
