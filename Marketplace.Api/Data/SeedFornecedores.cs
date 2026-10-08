using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// Fornecedores FICTÍCIOS (estudo) — um por categoria; Limpeza e Higiene vêm do mesmo distribuidor.
// Só roda se ainda não houver fornecedor. Liga cada produto ao fornecedor da sua categoria.
public static class SeedFornecedores
{
    private static readonly (string Nome, int Prazo, string Contato, string[] Categorias)[] Lista =
    [
        ("Padaria Trigo Dourado (fictícia)", 1, "Seu Antônio", ["Padaria"]),
        ("Hortifrúti Vale Verde (fictícia)", 1, "Dona Marta", ["Hortifrúti"]),
        ("Frigorífico Coxilha (fictício)", 2, "Rafael", ["Açougue"]),
        ("Laticínios Serra Alta (fictícia)", 2, "Cláudia", ["Frios e Laticínios"]),
        ("Distribuidora de Bebidas Guaíba (fictícia)", 3, "Júlio", ["Bebidas"]),
        ("Atacado Mercearia Sul (fictício)", 3, "Fernanda", ["Mercearia"]),
        ("Limpa & Cuida Distribuidora (fictícia)", 4, "Paulo", ["Limpeza", "Higiene e Beleza"]),
    ];

    public static async Task ExecutarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Fornecedores.AnyAsync()) return;

        var categorias = await db.Categorias.ToDictionaryAsync(c => c.Nome, c => c.Id);
        foreach (var (nome, prazo, contato, cats) in Lista)
        {
            var fornecedor = new Fornecedor { Nome = nome, PrazoEntregaDias = prazo, Contato = contato, Observacao = "Fornecedor fictício (dados de estudo)" };
            db.Fornecedores.Add(fornecedor);
            await db.SaveChangesAsync();

            var ids = cats.Where(categorias.ContainsKey).Select(c => categorias[c]).ToList();
            await db.Produtos.Where(p => ids.Contains(p.CategoriaId) && p.FornecedorId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.FornecedorId, fornecedor.Id));
        }
    }
}
