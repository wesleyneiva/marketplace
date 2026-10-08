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
    public DbSet<SessaoCaixa> SessoesCaixa => Set<SessaoCaixa>();
    public DbSet<MovimentoCaixa> MovimentosCaixa => Set<MovimentoCaixa>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<ItemVenda> ItensVenda => Set<ItemVenda>();
    public DbSet<PagamentoVenda> PagamentosVenda => Set<PagamentoVenda>();
    public DbSet<ClimaRegistro> Clima => Set<ClimaRegistro>();

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
            e.HasOne(m => m.Venda).WithMany().HasForeignKey(m => m.VendaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => m.VendaId);
        });

        builder.Entity<LoteValidade>(e =>
        {
            e.Property(l => l.QuantidadeInicial).HasPrecision(12, 3);
            e.Property(l => l.QuantidadeAtual).HasPrecision(12, 3);
            e.HasIndex(l => new { l.ProdutoId, l.DataValidade });
            e.HasIndex(l => l.DataValidade);

            e.HasOne(l => l.Produto).WithMany(p => p.Lotes).HasForeignKey(l => l.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ClimaRegistro>(e =>
        {
            e.ToTable("Clima");
            e.Property(c => c.Temperatura).HasPrecision(4, 1);
            e.Property(c => c.Chuva).HasPrecision(5, 1);
            e.HasIndex(c => c.DataHora).IsUnique(); // uma linha por hora
        });

        // ----- PDV -----
        builder.Entity<SessaoCaixa>(e =>
        {
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(s => s.ValorAbertura).HasPrecision(12, 2);
            e.Property(s => s.ValorEsperado).HasPrecision(12, 2);
            e.Property(s => s.ValorContado).HasPrecision(12, 2);
            e.Property(s => s.Diferenca).HasPrecision(12, 2);
            e.Property(s => s.ObservacaoFechamento).HasMaxLength(300);
            e.HasIndex(s => new { s.UsuarioId, s.Status });
            e.HasIndex(s => s.AbertaEm);

            // Regra no próprio banco: no máximo UMA sessão aberta por caixa físico
            // (índice único "parcial": só vale para as linhas com Status = 'Aberta').
            e.HasIndex(s => s.NumeroCaixa).IsUnique().HasFilter("\"Status\" = 'Aberta'")
                .HasDatabaseName("IX_SessoesCaixa_UmaAbertaPorCaixa");

            e.HasOne(s => s.Usuario).WithMany().HasForeignKey(s => s.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MovimentoCaixa>(e =>
        {
            e.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(15);
            e.Property(m => m.Valor).HasPrecision(12, 2);
            e.Property(m => m.Motivo).HasMaxLength(150);
            e.HasOne(m => m.SessaoCaixa).WithMany(s => s.Movimentos).HasForeignKey(m => m.SessaoCaixaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Usuario).WithMany().HasForeignKey(m => m.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Venda>(e =>
        {
            e.Property(v => v.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(v => v.Subtotal).HasPrecision(12, 2);
            e.Property(v => v.Desconto).HasPrecision(12, 2);
            e.Property(v => v.Total).HasPrecision(12, 2);
            e.Property(v => v.ValorPago).HasPrecision(12, 2);
            e.Property(v => v.Troco).HasPrecision(12, 2);
            e.Property(v => v.MotivoCancelamento).HasMaxLength(200);
            e.HasIndex(v => v.DataHora);
            e.HasOne(v => v.SessaoCaixa).WithMany(s => s.Vendas).HasForeignKey(v => v.SessaoCaixaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Usuario).WithMany().HasForeignKey(v => v.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.CanceladaPor).WithMany().HasForeignKey(v => v.CanceladaPorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ItemVenda>(e =>
        {
            e.Property(i => i.Descricao).HasMaxLength(150);
            e.Property(i => i.Unidade).HasMaxLength(3);
            e.Property(i => i.Quantidade).HasPrecision(12, 3);
            e.Property(i => i.PrecoUnitario).HasPrecision(10, 2);
            e.Property(i => i.CustoUnitario).HasPrecision(10, 2);
            e.Property(i => i.Total).HasPrecision(12, 2);
            e.HasOne(i => i.Venda).WithMany(v => v.Itens).HasForeignKey(i => i.VendaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Produto).WithMany().HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PagamentoVenda>(e =>
        {
            e.Property(p => p.Forma).HasConversion<string>().HasMaxLength(10);
            e.Property(p => p.Valor).HasPrecision(12, 2);
            e.HasOne(p => p.Venda).WithMany(v => v.Pagamentos).HasForeignKey(p => p.VendaId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
