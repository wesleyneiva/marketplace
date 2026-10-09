using System.Linq.Expressions;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// O DbContext é a "ponte" entre o C# e o banco de dados.
// Herdando de IdentityDbContext, ganhamos prontas as tabelas de usuários, perfis e vínculos.
// Cada DbSet<...> abaixo vira uma tabela.
public class AppDbContext(DbContextOptions<AppDbContext> options, ContextoEmpresa contexto)
    : IdentityDbContext<Usuario, IdentityRole, string>(options)
{
    // ----- Multi-tenant -----
    // Empresa de quem está usando agora (0 = nenhuma → as consultas voltam vazias).
    // O EF lê este valor A CADA consulta (não fica "congelado" no filtro).
    public int EmpresaAtual => contexto.EmpresaId ?? 0;
    public bool TemEmpresa => contexto.EmpresaId is not null;

    // Ordem alfabética do português (Açúcar, Água, Alface, Arroz), em vez da ordem "de computador".
    private const string OrdemPortugues = "pt-BR-x-icu";

    public DbSet<Empresa> Empresas => Set<Empresa>();
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
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<PedidoCompra> PedidosCompra => Set<PedidoCompra>();
    public DbSet<ItemPedidoCompra> ItensPedidoCompra => Set<ItemPedidoCompra>();
    public DbSet<NotaEntrada> NotasEntrada => Set<NotaEntrada>();
    public DbSet<VinculoFornecedorProduto> VinculosFornecedor => Set<VinculoFornecedorProduto>();
    public DbSet<ContaPagar> ContasPagar => Set<ContaPagar>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // unaccent: permite buscar "acucar" e achar "Açúcar".
        builder.HasPostgresExtension("unaccent");

        builder.Entity<Empresa>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Subdominio).HasMaxLength(40);
            e.HasIndex(x => x.Subdominio).IsUnique();
            e.Property(x => x.BalancaEtiqueta).HasMaxLength(10).HasDefaultValue(EtiquetaBalancaTipos.Preco);
            e.Property(x => x.BalancaDigitosCodigo).HasDefaultValue(4);
        });

        builder.Entity<Usuario>()
            .Property(u => u.NomeCompleto)
            .HasMaxLength(150);

        builder.Entity<Categoria>(e =>
        {
            e.Property(c => c.Nome).HasMaxLength(60).UseCollation(OrdemPortugues);
            e.HasIndex(c => new { c.EmpresaId, c.Nome }).IsUnique(); // o mesmo nome pode existir em outra empresa
        });

        builder.Entity<Produto>(e =>
        {
            e.Property(p => p.Nome).HasMaxLength(150).UseCollation(OrdemPortugues);
            e.Property(p => p.CodigoBarras).HasMaxLength(14);
            e.Property(p => p.Unidade).HasMaxLength(3);
            e.Property(p => p.Ncm).HasMaxLength(8);
            e.Property(p => p.Cest).HasMaxLength(7);
            e.Property(p => p.Cfop).HasMaxLength(4).HasDefaultValue("5102");
            e.Property(p => p.SituacaoTributaria).HasMaxLength(3);

            // Dinheiro: 2 casas decimais. Estoque: 3 casas (gramas, no caso do quilo).
            e.Property(p => p.PrecoCusto).HasPrecision(10, 2);
            e.Property(p => p.PrecoVenda).HasPrecision(10, 2);
            e.Property(p => p.EstoqueAtual).HasPrecision(12, 3);
            e.Property(p => p.EstoqueMinimo).HasPrecision(12, 3);

            // Dois produtos não podem ter o mesmo código de barras (mas vários podem não ter nenhum).
            e.HasIndex(p => new { p.EmpresaId, p.CodigoBarras }).IsUnique();
            // Um PLU por produto na loja (vários podem não ter balança).
            e.HasIndex(p => new { p.EmpresaId, p.CodigoBalanca }).IsUnique().HasFilter("\"CodigoBalanca\" IS NOT NULL");
            e.HasIndex(p => new { p.EmpresaId, p.Nome });

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
            e.HasIndex(m => new { m.EmpresaId, m.DataHora });

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

        // ----- Compras -----
        builder.Entity<Fornecedor>(e =>
        {
            e.Property(f => f.Nome).HasMaxLength(120).UseCollation(OrdemPortugues);
            e.HasIndex(f => new { f.EmpresaId, f.Nome }).IsUnique();
            e.Property(f => f.Cnpj).HasMaxLength(18);
            e.Property(f => f.Contato).HasMaxLength(100);
            e.Property(f => f.Telefone).HasMaxLength(30);
            e.Property(f => f.Email).HasMaxLength(150);
            e.Property(f => f.Observacao).HasMaxLength(300);
        });

        builder.Entity<Produto>()
            .HasOne(p => p.Fornecedor).WithMany(f => f.Produtos).HasForeignKey(p => p.FornecedorId).OnDelete(DeleteBehavior.SetNull);

        builder.Entity<PedidoCompra>(e =>
        {
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(p => p.Observacao).HasMaxLength(300);
            e.HasIndex(p => new { p.Status, p.PrevisaoEntrega });
            e.HasOne(p => p.Fornecedor).WithMany().HasForeignKey(p => p.FornecedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.CriadoPor).WithMany().HasForeignKey(p => p.CriadoPorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.RecebidoPor).WithMany().HasForeignKey(p => p.RecebidoPorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ItemPedidoCompra>(e =>
        {
            e.Property(i => i.Quantidade).HasPrecision(12, 3);
            e.Property(i => i.QuantidadeRecebida).HasPrecision(12, 3);
            e.Property(i => i.CustoUnitario).HasPrecision(10, 2);
            e.HasOne(i => i.PedidoCompra).WithMany(p => p.Itens).HasForeignKey(i => i.PedidoCompraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Produto).WithMany().HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });

        // ----- Entrada por XML da NF-e -----
        builder.Entity<NotaEntrada>(e =>
        {
            e.ToTable("NotasEntrada");
            e.Property(n => n.Chave).HasMaxLength(44).IsFixedLength();
            e.Property(n => n.Numero).HasMaxLength(9);
            e.Property(n => n.Serie).HasMaxLength(3);
            e.Property(n => n.ValorTotal).HasPrecision(12, 2);
            e.HasIndex(n => new { n.EmpresaId, n.Chave }).IsUnique(); // a mesma nota não entra duas vezes
            e.HasIndex(n => n.DataEmissao);
            e.HasOne(n => n.Fornecedor).WithMany().HasForeignKey(n => n.FornecedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.PedidoCompra).WithMany().HasForeignKey(n => n.PedidoCompraId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(n => n.RegistradaPor).WithMany().HasForeignKey(n => n.RegistradaPorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VinculoFornecedorProduto>(e =>
        {
            e.ToTable("VinculosFornecedor");
            e.Property(v => v.CodigoFornecedor).HasMaxLength(60);
            e.Property(v => v.Fator).HasPrecision(12, 4);
            e.HasIndex(v => new { v.EmpresaId, v.FornecedorId, v.CodigoFornecedor }).IsUnique();
            e.HasOne(v => v.Fornecedor).WithMany().HasForeignKey(v => v.FornecedorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.Produto).WithMany().HasForeignKey(v => v.ProdutoId).OnDelete(DeleteBehavior.Cascade);
        });

        // ----- Contas a pagar -----
        builder.Entity<ContaPagar>(e =>
        {
            e.ToTable("ContasPagar");
            e.Property(c => c.Descricao).HasMaxLength(200);
            e.Property(c => c.Documento).HasMaxLength(60);
            e.Property(c => c.Valor).HasPrecision(12, 2);
            e.Property(c => c.ValorPago).HasPrecision(12, 2);
            e.Property(c => c.FormaPagamento).HasMaxLength(40);
            e.Property(c => c.Observacao).HasMaxLength(300);
            e.HasIndex(c => new { c.EmpresaId, c.PagaEm, c.Vencimento }); // "abertas por vencimento" é a consulta mais comum
            e.HasOne(c => c.Fornecedor).WithMany().HasForeignKey(c => c.FornecedorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.NotaEntrada).WithMany().HasForeignKey(c => c.NotaEntradaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.PagaPor).WithMany().HasForeignKey(c => c.PagaPorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.CriadaPor).WithMany().HasForeignKey(c => c.CriadaPorId).OnDelete(DeleteBehavior.Restrict);
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
            e.HasIndex(s => new { s.EmpresaId, s.NumeroCaixa }).IsUnique().HasFilter("\"Status\" = 'Aberta'")
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
            e.Property(v => v.Origem).HasConversion<string>().HasMaxLength(10).HasDefaultValue(OrigemVenda.Caixa);
            e.HasIndex(v => new { v.EmpresaId, v.Origem, v.DataHora });
            e.Property(v => v.Subtotal).HasPrecision(12, 2);
            e.Property(v => v.Desconto).HasPrecision(12, 2);
            e.Property(v => v.Total).HasPrecision(12, 2);
            e.Property(v => v.ValorPago).HasPrecision(12, 2);
            e.Property(v => v.Troco).HasPrecision(12, 2);
            e.Property(v => v.MotivoCancelamento).HasMaxLength(200);
            e.HasIndex(v => new { v.EmpresaId, v.DataHora }); // relatórios: "vendas desta empresa no período"
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

        // ----- Multi-tenant: para TODA tabela com a etiqueta IDaEmpresa -----
        foreach (var tipo in builder.Model.GetEntityTypes().Where(t => typeof(IDaEmpresa).IsAssignableFrom(t.ClrType)).ToList())
        {
            // Chave estrangeira para Empresas: o banco não aceita EmpresaId que não existe.
            builder.Entity(tipo.ClrType).HasOne(typeof(Empresa)).WithMany().HasForeignKey(nameof(IDaEmpresa.EmpresaId))
                .OnDelete(DeleteBehavior.Restrict);

            // O FILTRO: toda consulta ganha "WHERE EmpresaId = <empresa atual>" sozinha.
            builder.Entity(tipo.ClrType).HasQueryFilter(Filtro(tipo.ClrType));
        }
    }

    // Monta  x => x.EmpresaId == EmpresaAtual  para o tipo da tabela.
    // Exceção: Usuarios. O login precisa achar o usuário pelo e-mail ANTES de saber a empresa
    // (então, sem empresa definida, a tabela de usuários fica visível — só para o login e para o
    // Identity conferir o cookie). Com empresa definida, o filtro vale igual às outras tabelas.
    private LambdaExpression Filtro(Type tipo)
    {
        var x = Expression.Parameter(tipo, "x");
        var daLinha = Expression.Property(x, nameof(IDaEmpresa.EmpresaId));
        var atual = Expression.Property(Expression.Constant(this), nameof(EmpresaAtual));
        Expression corpo = Expression.Equal(daLinha, atual);
        if (tipo == typeof(Usuario))
            corpo = Expression.OrElse(Expression.Not(Expression.Property(Expression.Constant(this), nameof(TemEmpresa))), corpo);
        return Expression.Lambda(corpo, x);
    }

    // Ao gravar: linha NOVA recebe a empresa atual sozinha; e nenhuma linha pode ir para outra empresa.
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CarimbarEmpresa();
        MarcarPrecosAlterados();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CarimbarEmpresa();
        MarcarPrecosAlterados();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    // Produto novo ou com preço de venda trocado (por qualquer tela, planilha ou nota) → anota a hora.
    private void MarcarPrecosAlterados()
    {
        foreach (var e in ChangeTracker.Entries<Produto>())
        {
            if (e.State == EntityState.Added
                || (e.State == EntityState.Modified && e.Property(p => p.PrecoVenda).IsModified
                    && e.Property(p => p.PrecoVenda).OriginalValue != e.Entity.PrecoVenda))
                e.Entity.PrecoAlteradoEm = DateTimeOffset.UtcNow;
        }
    }

    private void CarimbarEmpresa()
    {
        foreach (var e in ChangeTracker.Entries<IDaEmpresa>())
        {
            if (e.State == EntityState.Added && e.Entity.EmpresaId == 0)
            {
                e.Entity.EmpresaId = contexto.EmpresaId
                    ?? throw new InvalidOperationException($"Gravando {e.Entity.GetType().Name} sem empresa definida.");
            }
            else if (e.State is EntityState.Added or EntityState.Modified && TemEmpresa && e.Entity.EmpresaId != EmpresaAtual)
            {
                throw new InvalidOperationException($"Tentativa de gravar {e.Entity.GetType().Name} de outra empresa.");
            }
        }
    }
}
