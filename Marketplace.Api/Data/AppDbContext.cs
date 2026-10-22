using System.Linq.Expressions;
using System.Text.Json;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    public DbSet<Modelo> Modelos => Set<Modelo>();
    public DbSet<OrdemServico> OrdensServico => Set<OrdemServico>();
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
    public DbSet<CategoriaFinanceira> CategoriasFinanceiras => Set<CategoriaFinanceira>();
    public DbSet<ContaFinanceira> ContasFinanceiras => Set<ContaFinanceira>();
    public DbSet<MovimentoFinanceiro> MovimentosFinanceiros => Set<MovimentoFinanceiro>();
    public DbSet<ConfiguracaoRecebimento> ConfiguracoesRecebimento => Set<ConfiguracaoRecebimento>();
    public DbSet<Recebivel> Recebiveis => Set<Recebivel>();
    public DbSet<ExtratoBancario> ExtratosBancarios => Set<ExtratoBancario>();
    public DbSet<LinhaExtrato> LinhasExtrato => Set<LinhaExtrato>();
    public DbSet<CobrancaPix> CobrancasPix => Set<CobrancaPix>();
    public DbSet<AutorizacaoGerente> AutorizacoesGerente => Set<AutorizacaoGerente>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<LancamentoFiado> LancamentosFiado => Set<LancamentoFiado>();
    public DbSet<Plano> Planos => Set<Plano>();
    public DbSet<Fatura> Faturas => Set<Fatura>();
    public DbSet<ConfiguracaoPlataforma> ConfiguracoesPlataforma => Set<ConfiguracaoPlataforma>();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // unaccent: permite buscar "acucar" e achar "Açúcar".
        builder.HasPostgresExtension("unaccent");

        builder.Entity<Empresa>(e =>
        {
            e.Property(x => x.PixTipoChave).HasMaxLength(10);
            e.Property(x => x.PixChave).HasMaxLength(77);
            e.Property(x => x.PixNomeRecebedor).HasMaxLength(25);
            e.Property(x => x.PixAutomatico).HasMaxLength(20);
            e.Property(x => x.MercadoPagoToken).HasMaxLength(1000);
            e.Property(x => x.MercadoPagoTokenFinal).HasMaxLength(8);
            e.Property(x => x.MercadoPagoConta).HasMaxLength(120);
            e.Property(x => x.PixAvisoToken).HasMaxLength(40);
            e.HasIndex(x => x.PixAvisoToken).IsUnique().HasFilter("\"PixAvisoToken\" IS NOT NULL");
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Subdominio).HasMaxLength(40);
            e.HasIndex(x => x.Subdominio).IsUnique();
            e.Property(x => x.BalancaEtiqueta).HasMaxLength(10).HasDefaultValue(EtiquetaBalancaTipos.Preco);
            e.Property(x => x.BalancaDigitosCodigo).HasDefaultValue(4);
            e.Property(x => x.Cidade).HasMaxLength(80);
            e.Property(x => x.Uf).HasMaxLength(2);
            e.Property(x => x.Latitude).HasPrecision(8, 5);
            e.Property(x => x.Longitude).HasPrecision(8, 5);
            e.Property(x => x.Fuso).HasMaxLength(40).HasDefaultValue("America/Sao_Paulo");
            e.Property(x => x.SaldoMinimoCaixa).HasPrecision(14, 2).HasDefaultValue(0m);
            e.Property(x => x.AliquotaSimples).HasPrecision(6, 4).HasDefaultValue(0m);
            e.Property(x => x.VendasAutomaticas).HasMaxLength(12).HasDefaultValue(ModosVendasAutomaticas.Desligado);
            e.Property(x => x.Ramo).HasMaxLength(20).HasDefaultValue(Ramos.Mercado);
            e.HasIndex(x => x.Plataforma).IsUnique().HasFilter("\"Plataforma\""); // no máximo uma empresa da plataforma
            e.Property(x => x.RazaoSocial).HasMaxLength(150);
            e.Property(x => x.Documento).HasMaxLength(14);
            e.Property(x => x.Responsavel).HasMaxLength(100);
            e.Property(x => x.Telefone).HasMaxLength(13);
            e.Property(x => x.EmailContato).HasMaxLength(150);
            e.Property(x => x.Endereco).HasMaxLength(250);
            e.Property(x => x.Observacoes).HasMaxLength(1000);
            e.Property(x => x.DiaVencimento).HasDefaultValue(10);
            e.Property(x => x.PrecoCombinado).HasPrecision(10, 2);
            e.Property(x => x.MotivoSuspensao).HasMaxLength(10);
            e.HasOne(x => x.Plano).WithMany().HasForeignKey(x => x.PlanoId).OnDelete(DeleteBehavior.Restrict);

            // Horário: guardado como JSON (jsonb). O "comparador" compara o JSON, para o EF perceber quando muda.
            e.Property(x => x.Horario).HasColumnType("jsonb").HasConversion(
                h => JsonSerializer.Serialize(h, Json),
                texto => JsonSerializer.Deserialize<HorarioFuncionamento>(texto, Json) ?? HorarioFuncionamento.Padrao(),
                new ValueComparer<HorarioFuncionamento>(
                    (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                    h => JsonSerializer.Serialize(h, Json).GetHashCode(),
                    h => JsonSerializer.Deserialize<HorarioFuncionamento>(JsonSerializer.Serialize(h, Json), Json)!));
        });

        // ----- Assinaturas do SaaS (da plataforma: sem filtro de empresa) -----
        builder.Entity<Plano>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(60);
            e.HasIndex(x => x.Nome).IsUnique();
            e.Property(x => x.Descricao).HasMaxLength(300);
            e.Property(x => x.PrecoMensal).HasPrecision(10, 2);
            e.Property(x => x.PrecoCaixaExtra).HasPrecision(10, 2);
        });
        builder.Entity<Fatura>(e =>
        {
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Valor).HasPrecision(10, 2);
            e.Property(x => x.ValorPago).HasPrecision(10, 2);
            e.Property(x => x.Descricao).HasMaxLength(150);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.FormaPagamento).HasMaxLength(15);
            e.Property(x => x.Observacao).HasMaxLength(300);
            // Uma mensalidade (não cancelada) por cliente e mês.
            e.HasIndex(x => new { x.ClienteId, x.Competencia }).IsUnique().HasFilter("\"Status\" <> 'Cancelada'");
            e.HasIndex(x => new { x.Status, x.Vencimento });
        });
        builder.Entity<ConfiguracaoPlataforma>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ChavePix).HasMaxLength(100);
            e.Property(x => x.NomeRecebedor).HasMaxLength(100);
            e.Property(x => x.ContatoCobranca).HasMaxLength(20);
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
            // Grade: uma variação por tamanho + cor dentro do modelo.
            e.Property(p => p.Tamanho).HasMaxLength(20);
            e.Property(p => p.Cor).HasMaxLength(40);
            e.HasIndex(p => new { p.EmpresaId, p.ModeloId, p.Tamanho, p.Cor }).IsUnique().HasFilter("\"ModeloId\" IS NOT NULL");
            e.HasOne(p => p.Modelo).WithMany(m => m.Variacoes).HasForeignKey(p => p.ModeloId).OnDelete(DeleteBehavior.Restrict);

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
            e.Property(c => c.Latitude).HasPrecision(6, 2);
            e.Property(c => c.Longitude).HasPrecision(6, 2);
            e.HasIndex(c => new { c.Latitude, c.Longitude, c.DataHora }).IsUnique(); // uma linha por hora, por lugar
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

        builder.Entity<OrdemServico>(e =>
        {
            e.ToTable("OrdensServico");
            e.Property(o => o.Cliente).HasMaxLength(100);
            e.Property(o => o.Telefone).HasMaxLength(20);
            e.Property(o => o.Raquete).HasMaxLength(120);
            e.Property(o => o.Corda).HasMaxLength(120);
            e.Property(o => o.Tensao).HasMaxLength(20);
            e.Property(o => o.Servico).HasMaxLength(150);
            e.Property(o => o.Observacao).HasMaxLength(300);
            e.Property(o => o.Status).HasMaxLength(12);
            e.Property(o => o.Valor).HasPrecision(10, 2);
            e.HasIndex(o => new { o.EmpresaId, o.Numero }).IsUnique();
            e.HasIndex(o => new { o.EmpresaId, o.Status, o.Prazo });
            e.HasIndex(o => new { o.EmpresaId, o.Telefone });
        });

        builder.Entity<Modelo>(e =>
        {
            e.Property(m => m.Nome).HasMaxLength(120).UseCollation(OrdemPortugues);
            e.Property(m => m.Referencia).HasMaxLength(40);
            e.Property(m => m.Marca).HasMaxLength(60);
            e.HasIndex(m => new { m.EmpresaId, m.Nome });
            e.HasOne(m => m.Categoria).WithMany().HasForeignKey(m => m.CategoriaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Fornecedor).WithMany().HasForeignKey(m => m.FornecedorId).OnDelete(DeleteBehavior.SetNull);
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
            e.HasOne(c => c.CategoriaFinanceira).WithMany().HasForeignKey(c => c.CategoriaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.ContaFinanceira).WithMany().HasForeignKey(c => c.ContaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
        });

        // ----- Financeiro -----
        builder.Entity<CategoriaFinanceira>(e =>
        {
            e.ToTable("CategoriasFinanceiras");
            e.Property(c => c.Codigo).HasMaxLength(10);
            e.Property(c => c.Nome).HasMaxLength(80).UseCollation(OrdemPortugues);
            e.Property(c => c.Grupo).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Natureza).HasConversion<string>().HasMaxLength(10);
            e.Property(c => c.Chave).HasMaxLength(30);
            e.HasIndex(c => new { c.EmpresaId, c.Nome }).IsUnique();
            e.HasIndex(c => new { c.EmpresaId, c.Codigo }).IsUnique();
            e.HasIndex(c => new { c.EmpresaId, c.Chave }).IsUnique().HasFilter("\"Chave\" IS NOT NULL");
        });

        builder.Entity<ContaFinanceira>(e =>
        {
            e.ToTable("ContasFinanceiras");
            e.Property(c => c.Nome).HasMaxLength(80).UseCollation(OrdemPortugues);
            e.Property(c => c.Tipo).HasConversion<string>().HasMaxLength(12);
            e.Property(c => c.Banco).HasMaxLength(60);
            e.Property(c => c.Agencia).HasMaxLength(10);
            e.Property(c => c.Numero).HasMaxLength(20);
            e.Property(c => c.SaldoInicial).HasPrecision(14, 2);
            e.Property(c => c.Chave).HasMaxLength(30);
            e.HasIndex(c => new { c.EmpresaId, c.Nome }).IsUnique();
            e.HasIndex(c => new { c.EmpresaId, c.Chave }).IsUnique().HasFilter("\"Chave\" IS NOT NULL");
        });

        builder.Entity<MovimentoFinanceiro>(e =>
        {
            e.ToTable("MovimentosFinanceiros");
            e.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(10);
            e.Property(m => m.Valor).HasPrecision(14, 2);
            e.Property(m => m.Descricao).HasMaxLength(200);
            e.Property(m => m.Documento).HasMaxLength(60);
            e.Property(m => m.Origem).HasConversion<string>().HasMaxLength(15);
            // Regra no próprio banco: valor sempre positivo (o Tipo diz se entrou ou saiu).
            e.ToTable(t => t.HasCheckConstraint("CK_MovimentosFinanceiros_ValorPositivo", "\"Valor\" > 0"));
            e.HasIndex(m => new { m.EmpresaId, m.Data });                  // extrato do período
            e.HasIndex(m => new { m.ContaFinanceiraId, m.Data });          // saldo / extrato de uma conta
            e.HasIndex(m => m.ContaPagarId);
            e.HasIndex(m => m.TransferenciaId);
            e.HasIndex(m => m.SessaoCaixaId);
            e.HasIndex(m => m.LiquidacaoId);
            e.HasIndex(m => m.ConciliacaoId);
            e.HasOne(m => m.SessaoCaixa).WithMany().HasForeignKey(m => m.SessaoCaixaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.ContaFinanceira).WithMany().HasForeignKey(m => m.ContaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.CategoriaFinanceira).WithMany().HasForeignKey(m => m.CategoriaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.ContaPagar).WithMany().HasForeignKey(m => m.ContaPagarId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.CriadoPor).WithMany().HasForeignKey(m => m.CriadoPorId).OnDelete(DeleteBehavior.Restrict);
        });

        // ----- Autorizações do gerente no caixa (cancelar item, compra e venda) -----
        builder.Entity<AutorizacaoGerente>(e =>
        {
            e.ToTable("AutorizacoesGerente");
            e.Property(x => x.Acao).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.Motivo).HasMaxLength(200);
            e.Property(x => x.Descricao).HasMaxLength(200);
            e.Property(x => x.Valor).HasPrecision(14, 2);
            e.HasIndex(x => new { x.EmpresaId, x.DataHora });
            e.HasIndex(x => new { x.EmpresaId, x.IdLocal }).IsUnique().HasFilter("\"IdLocal\" IS NOT NULL");
            e.HasOne(x => x.SessaoCaixa).WithMany().HasForeignKey(x => x.SessaoCaixaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Operador).WithMany().HasForeignKey(x => x.OperadorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AutorizadoPor).WithMany().HasForeignKey(x => x.AutorizadoPorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Venda).WithMany().HasForeignKey(x => x.VendaId).OnDelete(DeleteBehavior.Restrict);
        });

        // ----- Pix automático (Mercado Pago) -----
        builder.Entity<CobrancaPix>(e =>
        {
            e.ToTable("CobrancasPix");
            e.Property(x => x.Provedor).HasMaxLength(20);
            e.Property(x => x.IdExterno).HasMaxLength(40);
            e.Property(x => x.Valor).HasPrecision(14, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.StatusExterno).HasMaxLength(30);
            e.HasIndex(x => new { x.Provedor, x.IdExterno }).IsUnique().HasFilter("\"IdExterno\" IS NOT NULL");
            e.HasIndex(x => x.Referencia).IsUnique();
            e.HasIndex(x => new { x.SessaoCaixaId, x.Status });
            e.HasIndex(x => x.VendaId).IsUnique().HasFilter("\"VendaId\" IS NOT NULL");
            e.HasOne(x => x.SessaoCaixa).WithMany().HasForeignKey(x => x.SessaoCaixaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Venda).WithMany().HasForeignKey(x => x.VendaId).OnDelete(DeleteBehavior.Restrict);
        });

        // ----- Conciliação bancária (extrato OFX) -----
        builder.Entity<ExtratoBancario>(e =>
        {
            e.ToTable("ExtratosBancarios");
            e.Property(x => x.NomeArquivo).HasMaxLength(200);
            e.Property(x => x.Banco).HasMaxLength(10);
            e.Property(x => x.Agencia).HasMaxLength(20);
            e.Property(x => x.NumeroConta).HasMaxLength(40);
            e.Property(x => x.SaldoFinal).HasPrecision(14, 2);
            e.HasIndex(x => new { x.ContaFinanceiraId, x.ImportadoEm });
            e.HasOne(x => x.ContaFinanceira).WithMany().HasForeignKey(x => x.ContaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ImportadoPor).WithMany().HasForeignKey(x => x.ImportadoPorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LinhaExtrato>(e =>
        {
            e.ToTable("LinhasExtrato");
            e.Property(x => x.Valor).HasPrecision(14, 2);
            e.Property(x => x.Descricao).HasMaxLength(200);
            e.Property(x => x.Documento).HasMaxLength(60);
            e.Property(x => x.TipoBanco).HasMaxLength(20);
            e.Property(x => x.Chave).HasMaxLength(120);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => new { x.ContaFinanceiraId, x.Chave }).IsUnique(); // a mesma linha não entra duas vezes
            e.HasIndex(x => new { x.ContaFinanceiraId, x.Status, x.Data });
            e.HasIndex(x => x.ConciliacaoId);
            e.HasOne(x => x.ExtratoBancario).WithMany().HasForeignKey(x => x.ExtratoBancarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ContaFinanceira>().WithMany().HasForeignKey(x => x.ContaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ConfiguracaoRecebimento>(e =>
        {
            e.ToTable("ConfiguracoesRecebimento");
            e.Property(c => c.Forma).HasConversion<string>().HasMaxLength(10);
            e.Property(c => c.TaxaPercentual).HasPrecision(6, 4);
            e.HasIndex(c => new { c.EmpresaId, c.Forma }).IsUnique(); // uma configuração por forma
            e.ToTable(t => t.HasCheckConstraint("CK_ConfiguracoesRecebimento_Valores",
                "\"TaxaPercentual\" >= 0 AND \"TaxaPercentual\" < 100 AND \"PrazoDias\" >= 0 AND \"PrazoDias\" <= 365"));
            e.HasOne(c => c.ContaFinanceira).WithMany().HasForeignKey(c => c.ContaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Recebivel>(e =>
        {
            e.ToTable("Recebiveis");
            e.Property(r => r.Forma).HasConversion<string>().HasMaxLength(10);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(r => r.ValorBruto).HasPrecision(14, 2);
            e.Property(r => r.TaxaPercentual).HasPrecision(6, 4);
            e.Property(r => r.Taxa).HasPrecision(14, 2);
            e.Property(r => r.ValorLiquido).HasPrecision(14, 2);
            e.HasIndex(r => new { r.EmpresaId, r.Status, r.Previsao }); // "o que vence até hoje e não caiu"
            e.HasIndex(r => new { r.SessaoCaixaId, r.Forma }).IsUnique(); // um recebível por caixa e forma (nunca lança duas vezes)
            e.HasIndex(r => r.LiquidacaoId);
            e.HasOne(r => r.SessaoCaixa).WithMany().HasForeignKey(r => r.SessaoCaixaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.ContaFinanceira).WithMany().HasForeignKey(r => r.ContaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
        });

        // ----- Fiado -----
        builder.Entity<Cliente>(e =>
        {
            e.Property(c => c.Nome).HasMaxLength(120).UseCollation(OrdemPortugues);
            e.Property(c => c.Apelido).HasMaxLength(60);
            e.Property(c => c.Telefone).HasMaxLength(20);
            e.Property(c => c.Cpf).HasMaxLength(11);
            e.Property(c => c.Endereco).HasMaxLength(200);
            e.Property(c => c.Observacao).HasMaxLength(300);
            e.Property(c => c.LimiteCredito).HasPrecision(12, 2);
            e.HasIndex(c => new { c.EmpresaId, c.Nome });
            e.HasIndex(c => new { c.EmpresaId, c.Cpf }).IsUnique().HasFilter("\"Cpf\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_Clientes_DiaPagamento", "\"DiaPagamento\" BETWEEN 1 AND 28"));
        });

        builder.Entity<LancamentoFiado>(e =>
        {
            e.ToTable("LancamentosFiado");
            e.Property(l => l.Tipo).HasConversion<string>().HasMaxLength(10);
            e.Property(l => l.Forma).HasConversion<string>().HasMaxLength(10);
            e.Property(l => l.Valor).HasPrecision(12, 2);
            e.Property(l => l.Observacao).HasMaxLength(200);
            e.ToTable(t => t.HasCheckConstraint("CK_LancamentosFiado_ValorPositivo", "\"Valor\" > 0"));
            e.HasIndex(l => new { l.ClienteId, l.DataHora });
            e.HasIndex(l => l.VendaId);
            e.HasIndex(l => l.SessaoCaixaId);
            e.HasOne(l => l.Cliente).WithMany().HasForeignKey(l => l.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Venda).WithMany().HasForeignKey(l => l.VendaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.SessaoCaixa).WithMany().HasForeignKey(l => l.SessaoCaixaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.MovimentoFinanceiro).WithMany().HasForeignKey(l => l.MovimentoFinanceiroId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Usuario).WithMany().HasForeignKey(l => l.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Venda>().HasOne(v => v.Cliente).WithMany().HasForeignKey(v => v.ClienteId).OnDelete(DeleteBehavior.Restrict);

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
            e.HasIndex(v => new { v.EmpresaId, v.IdLocal }).IsUnique().HasFilter("\"IdLocal\" IS NOT NULL"); // caixa offline: sem venda dupla
            e.Property(v => v.Subtotal).HasPrecision(12, 2);
            e.Property(v => v.Desconto).HasPrecision(12, 2);
            e.Property(v => v.Total).HasPrecision(12, 2);
            e.Property(v => v.ValorPago).HasPrecision(12, 2);
            e.Property(v => v.Troco).HasPrecision(12, 2);
            e.Property(v => v.MotivoCancelamento).HasMaxLength(200);
            // Relatórios: "vendas desta empresa no período". O INCLUDE traz situação, total e troco no próprio índice
            // (o banco não precisa ir à tabela; teste de estresse de 19/10/2026).
            e.HasIndex(v => new { v.EmpresaId, v.DataHora }).IncludeProperties(v => new { v.Status, v.Total, v.Troco });
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
            // Relatórios e DRE somam os itens de cada venda: o índice já traz o que somam.
            e.HasIndex(i => i.VendaId).IncludeProperties(i => new { i.ProdutoId, i.Quantidade, i.CustoUnitario, i.Total });
        });

        builder.Entity<PagamentoVenda>(e =>
        {
            e.Property(p => p.Forma).HasConversion<string>().HasMaxLength(10);
            e.Property(p => p.Valor).HasPrecision(12, 2);
            e.HasOne(p => p.Venda).WithMany(v => v.Pagamentos).HasForeignKey(p => p.VendaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.VendaId).IncludeProperties(p => new { p.Forma, p.Valor }); // "formas de pagamento" (4x mais rápido)
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
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CarimbarEmpresa();
        MarcarPrecosAlterados();
        var empresasDaDre = EmpresasComDreAlterada();
        var linhas = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var id in empresasDaDre) Services.CacheCurto.FinanceiroMudou(id);
        return linhas;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CarimbarEmpresa();
        MarcarPrecosAlterados();
        var empresasDaDre = EmpresasComDreAlterada();
        var linhas = base.SaveChanges(acceptAllChangesOnSuccess);
        foreach (var id in empresasDaDre) Services.CacheCurto.FinanceiroMudou(id);
        return linhas;
    }

    // Gravações que mudam a DRE: o financeiro, contas a pagar, perdas/ajustes de estoque, venda cancelada e a
    // alíquota da empresa. Venda NOVA não entra (o caixa grava o tempo todo; a DRE do mês aberto aceita 2 min de atraso).
    private List<int> EmpresasComDreAlterada() => ChangeTracker.Entries()
        .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
        .Where(e => e.Entity switch
        {
            MovimentoFinanceiro or ContaPagar or CategoriaFinanceira or ContaFinanceira or Recebivel or Empresa => true,
            Venda => e.State != EntityState.Added,
            MovimentacaoEstoque m => m.Tipo is not (TipoMovimentacao.Venda or TipoMovimentacao.Entrada),
            _ => false,
        })
        .Select(e => e.Entity is Empresa emp ? emp.Id : e.Entity is IDaEmpresa d ? d.EmpresaId : 0)
        .Where(id => id > 0).Distinct().ToList();

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
