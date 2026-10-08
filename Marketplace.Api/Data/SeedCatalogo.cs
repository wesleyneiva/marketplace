using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// Catálogo FICTÍCIO do mercadinho, para estudo. Só roda se ainda não houver nenhuma categoria.
// Os códigos de barras começam com "20": faixa do EAN-13 reservada para uso interno da loja,
// então nunca coincidem com produtos reais.
public static class SeedCatalogo
{
    // (nome, unidade, custo, venda, estoque, mínimo, perecível?, tem código de barras?)
    private record Item(string Nome, string Unidade, decimal Custo, decimal Venda,
        decimal Estoque, decimal Minimo, bool Perecivel, bool ComCodigo = true);

    private static readonly Dictionary<string, Item[]> Catalogo = new()
    {
        ["Mercearia"] =
        [
            new("Arroz branco tipo 1 5kg", "UN", 22.90m, 29.90m, 40, 15, false),
            new("Feijão preto 1kg", "UN", 6.20m, 8.99m, 60, 20, false),
            new("Feijão carioca 1kg", "UN", 6.50m, 9.49m, 35, 15, false),
            new("Açúcar refinado 1kg", "UN", 3.90m, 5.49m, 50, 20, false),
            new("Café torrado e moído 500g", "UN", 14.50m, 21.90m, 30, 12, false),
            new("Óleo de soja 900ml", "UN", 5.80m, 7.99m, 48, 18, false),
            new("Macarrão espaguete 500g", "UN", 3.20m, 4.99m, 55, 20, false),
            new("Farinha de trigo 1kg", "UN", 4.10m, 5.99m, 25, 10, false),
            new("Sal refinado 1kg", "UN", 1.80m, 2.99m, 30, 10, false),
            new("Molho de tomate 340g", "UN", 2.10m, 3.49m, 70, 25, false),
            new("Leite condensado 395g", "UN", 4.90m, 7.29m, 40, 15, false),
            new("Achocolatado em pó 400g", "UN", 6.90m, 9.99m, 8, 10, false),
        ],
        ["Bebidas"] =
        [
            new("Água mineral sem gás 500ml", "UN", 0.90m, 2.49m, 120, 48, false),
            new("Refrigerante de cola 2L", "UN", 6.40m, 9.99m, 60, 24, false),
            new("Refrigerante de guaraná 2L", "UN", 5.20m, 8.49m, 45, 24, false),
            new("Suco de uva integral 1L", "UN", 9.80m, 14.90m, 18, 8, true),
            new("Cerveja pilsen lata 350ml", "UN", 2.90m, 4.49m, 240, 96, true),
            new("Cerveja puro malte long neck 355ml", "UN", 4.60m, 7.49m, 96, 48, true),
            new("Energético 250ml", "UN", 5.10m, 8.99m, 30, 12, false),
            new("Água de coco 1L", "UN", 6.80m, 10.99m, 15, 6, true),
        ],
        ["Hortifrúti"] =
        [
            new("Banana prata", "KG", 4.20m, 6.99m, 25.5m, 10, true, false),
            new("Tomate", "KG", 5.90m, 8.99m, 18, 8, true, false),
            new("Batata", "KG", 3.80m, 5.99m, 30, 12, true, false),
            new("Cebola", "KG", 3.50m, 5.49m, 22, 8, true, false),
            new("Maçã gala", "KG", 7.90m, 11.99m, 15, 6, true, false),
            new("Laranja", "KG", 3.20m, 4.99m, 28, 10, true, false),
            new("Alface crespa", "UN", 1.90m, 3.49m, 20, 10, true, false),
        ],
        ["Padaria"] =
        [
            new("Pão francês", "KG", 9.50m, 16.90m, 8, 3, true, false),
            new("Pão de forma 500g", "UN", 6.90m, 10.49m, 4, 6, true),
            new("Bolo de laranja", "UN", 8.00m, 14.90m, 6, 2, true, false),
            new("Biscoito cream cracker 350g", "UN", 3.60m, 5.79m, 35, 12, true),
            new("Torrada integral 160g", "UN", 4.20m, 6.99m, 12, 5, true),
        ],
        ["Frios e Laticínios"] =
        [
            new("Leite integral 1L", "UN", 4.60m, 6.29m, 96, 36, true),
            new("Iogurte natural 170g", "UN", 1.90m, 3.29m, 40, 15, true),
            new("Queijo mussarela fatiado", "KG", 38.00m, 59.90m, 6.5m, 2, true, false),
            new("Presunto cozido fatiado", "KG", 28.00m, 44.90m, 5.2m, 2, true, false),
            new("Manteiga com sal 200g", "UN", 9.90m, 14.99m, 18, 6, true),
            new("Requeijão cremoso 200g", "UN", 6.20m, 9.49m, 20, 8, true),
            new("Ovos brancos", "DZ", 9.50m, 13.99m, 10, 12, true),
        ],
        ["Açougue"] =
        [
            new("Carne moída de patinho", "KG", 34.90m, 49.90m, 12, 5, true, false),
            new("Peito de frango", "KG", 15.90m, 22.99m, 20, 8, true, false),
            new("Linguiça toscana", "KG", 19.90m, 29.90m, 10, 4, true, false),
            new("Costela bovina", "KG", 26.90m, 39.90m, 15, 5, true, false),
            new("Coxa e sobrecoxa de frango", "KG", 11.90m, 17.99m, 18, 6, true, false),
        ],
        ["Limpeza"] =
        [
            new("Detergente líquido 500ml", "UN", 1.90m, 2.99m, 60, 24, false),
            new("Sabão em pó 1,6kg", "UN", 15.90m, 24.90m, 20, 8, false),
            new("Água sanitária 2L", "UN", 4.50m, 6.99m, 25, 10, false),
            new("Desinfetante 2L", "UN", 6.90m, 10.49m, 18, 8, false),
            new("Esponja multiuso (3 un.)", "PCT", 2.80m, 4.99m, 30, 10, false),
            new("Papel toalha (2 rolos)", "PCT", 5.40m, 8.49m, 20, 8, false),
        ],
        ["Higiene e Beleza"] =
        [
            new("Papel higiênico (12 rolos)", "PCT", 14.90m, 22.90m, 25, 10, false),
            new("Sabonete 85g", "UN", 1.60m, 2.79m, 80, 30, false),
            new("Creme dental 90g", "UN", 3.20m, 5.49m, 45, 18, false),
            new("Shampoo 350ml", "UN", 11.90m, 18.90m, 15, 6, false),
            new("Desodorante aerosol 150ml", "UN", 10.50m, 16.99m, 20, 8, false),
        ],
    };

    public static async Task ExecutarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedCatalogo");

        if (await db.Categorias.AnyAsync())
            return;

        var sequencia = 1;
        foreach (var (nomeCategoria, itens) in Catalogo)
        {
            var categoria = new Categoria { Nome = nomeCategoria };
            foreach (var item in itens)
            {
                categoria.Produtos.Add(new Produto
                {
                    Nome = item.Nome,
                    CodigoBarras = item.ComCodigo ? GerarEan13Interno(sequencia) : null,
                    Unidade = item.Unidade,
                    PrecoCusto = item.Custo,
                    PrecoVenda = item.Venda,
                    EstoqueAtual = item.Estoque,
                    EstoqueMinimo = item.Minimo,
                    ControlaValidade = item.Perecivel,
                });
                sequencia++;
            }
            db.Categorias.Add(categoria);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Catálogo fictício criado: {Categorias} categorias, {Produtos} produtos.",
            Catalogo.Count, sequencia - 1);
    }

    // EAN-13 = 12 dígitos + 1 dígito verificador (calculado com pesos 1 e 3 alternados).
    private static string GerarEan13Interno(int sequencia)
    {
        var doze = $"20{sequencia:D10}";
        var soma = doze.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        var verificador = (10 - soma % 10) % 10;
        return doze + verificador;
    }
}
