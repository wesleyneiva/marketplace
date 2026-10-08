using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// O DbContext é a "ponte" entre o C# e o banco de dados.
// Herdando de IdentityDbContext, ganhamos prontas as tabelas de usuários, perfis e vínculos.
// Cada DbSet<...> abaixo vira uma tabela.
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<Usuario, IdentityRole, string>(options)
{
    // Ordem alfabética do português (Açúcar, Água, Alface, Arroz), em vez da ordem "de computador".
    private const string OrdemPortugues = "pt-BR-x-icu";

    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<MovimentacaoEstoque> Movimentacoes => Set<MovimentacaoEstoque>();
    public DbSet<LoteValidade> Lotes => Set<LoteValidade>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // unaccent: permite buscar "acucar" e achar "Açúcar".
        builder.HasPostgresExtension("unaccent");

        builder.Entity<Usuario>()
            .Property(u => u.NomeCompleto)
            .HasMaxLength(150);

        builder.Entity<Categoria>(e =>
        {
            e.Property(c => c.Nome).HasMaxLength(60).UseCollation(OrdemPortugues);
            e.HasIndex(c => c.Nome).IsUnique();
        });

        builder.Entity<Produto>(e =>
        {
            e.Property(p => p.Nome).HasMaxLength(150).UseCollation(OrdemPortugues);
            e.Property(p => p.CodigoBarras).HasMaxLength(14);
            e.Property(p => p.Unidade).HasMaxLength(3);

            // Dinheiro: 2 casas decimais. Estoque: 3 casas (gramas, no caso do quilo).
            e.Property(p => p.PrecoCusto).HasPrecision(10, 2);
            e.Property(p => p.PrecoVenda).HasPrecision(10, 2);
            e.Property(p => p.EstoqueAtual).HasPrecision(12, 3);
            e.Property(p => p.EstoqueMinimo).HasPrecision(12, 3);

            // Dois produtos não podem ter o mesmo código de barras (mas vários podem não ter nenhum).
            e.HasIndex(p => p.CodigoBarras).IsUnique();
            e.HasIndex(p => p.Nome);

            // xmin do PostgreSQL como "versão" da linha (controle de concorrência).
            e.Property(p => p.Versao).IsRowVersion();

            // Não deixa apagar uma categoria que ainda tem produtos.
            e.HasOne(p => p.Categoria)
                .WithMany(c => c.Produtos)
                .HasForeignKey(p => p.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MovimentacaoEstoque>(e =>
        {
            // Guarda o tipo como texto ("Entrada", "Perda"...) — fica legível no banco e no n8n.
            e.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.Quantidade).HasPrecision(12, 3);
            e.Property(m => m.EstoqueAnterior).HasPrecision(12, 3);
            e.Property(m => m.EstoquePosterior).HasPrecision(12, 3);
            e.Property(m => m.CustoUnitario).HasPrecision(10, 2);
            e.Property(m => m.Motivo).HasMaxLength(30);
            e.Property(m => m.Observacao).HasMaxLength(300);

            e.HasIndex(m => new { m.ProdutoId, m.DataHora });
            e.HasIndex(m => m.DataHora);

            e.HasOne(m => m.Produto).WithMany().HasForeignKey(m => m.ProdutoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Lote).WithMany().HasForeignKey(m => m.LoteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Usuario).WithMany().HasForeignKey(m => m.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LoteValidade>(e =>
        {
            e.Property(l => l.QuantidadeInicial).HasPrecision(12, 3);
            e.Property(l => l.QuantidadeAtual).HasPrecision(12, 3);
            e.HasIndex(l => new { l.ProdutoId, l.DataValidade });
            e.HasIndex(l => l.DataValidade);

            e.HasOne(l => l.Produto).WithMany(p => p.Lotes).HasForeignKey(l => l.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
