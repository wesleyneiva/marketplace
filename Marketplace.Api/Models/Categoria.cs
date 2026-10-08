namespace Marketplace.Api.Models;

// Seção do mercado (Mercearia, Bebidas, Hortifrúti...).
public class Categoria
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public bool Ativa { get; set; } = true;

    // Navegação: "os produtos desta categoria" (o EF preenche quando pedimos).
    public List<Produto> Produtos { get; set; } = [];
}
