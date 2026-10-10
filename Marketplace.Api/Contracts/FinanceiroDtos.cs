using System.ComponentModel.DataAnnotations;
using Marketplace.Api.Models;

namespace Marketplace.Api.Contracts;

// ----- Contas financeiras (onde o dinheiro está) -----

public record ContaFinanceiraResponse(
    int Id, string Nome, TipoContaFinanceira Tipo, string? Banco, string? Agencia, string? Numero,
    decimal SaldoInicial, DateOnly SaldoInicialEm, decimal Saldo, int Movimentos, bool DoSistema, bool Ativa);

public record ContaFinanceiraRequest(
    [Required(ErrorMessage = "Informe o nome.")][StringLength(80, MinimumLength = 2, ErrorMessage = "Nome: de 2 a 80 letras.")] string Nome,
    TipoContaFinanceira Tipo,
    [StringLength(60)] string? Banco,
    [StringLength(10)] string? Agencia,
    [StringLength(20)] string? Numero,
    [Range(-99999999, 99999999, ErrorMessage = "Saldo inicial inválido.")] decimal SaldoInicial,
    DateOnly SaldoInicialEm);

// ----- Plano de contas -----

public record CategoriaFinanceiraResponse(
    int Id, string Codigo, string Nome, GrupoFinanceiro Grupo, string NomeGrupo, NaturezaFinanceira Natureza,
    bool DoSistema, bool Ativa, int Usos);

// Natureza só é escolhida no grupo "Fora do resultado" (empréstimo recebido × pago); nos outros vem do grupo.
public record CategoriaFinanceiraRequest(
    [Required(ErrorMessage = "Informe o nome.")][StringLength(80, MinimumLength = 2, ErrorMessage = "Nome: de 2 a 80 letras.")] string Nome,
    GrupoFinanceiro Grupo,
    NaturezaFinanceira? Natureza);

public record GrupoFinanceiroResponse(GrupoFinanceiro Grupo, int Numero, string Nome, NaturezaFinanceira? Natureza, string Explicacao);

public record SugestaoCategoriaResponse(int? CategoriaId, string? Nome);

// ----- O livro -----

public record MovimentoFinanceiroResponse(
    int Id, DateOnly Data, TipoMovimentoFinanceiro Tipo, decimal Valor, int ContaId, string Conta,
    int? CategoriaId, string? Categoria, string? CodigoCategoria, string Descricao, string? Documento,
    OrigemMovimentoFinanceiro Origem, int? ContaPagarId, Guid? TransferenciaId, bool Conciliado,
    string? CriadoPor, decimal? SaldoApos);

// Extrato: com uma conta só, vem também o saldo antes do período e o saldo linha a linha.
// Implantacao: o saldo inicial da conta, quando a data dele cai DENTRO do período (vira uma linha própria no extrato).
public record ExtratoResponse(
    DateOnly De, DateOnly Ate, decimal Entradas, decimal Saidas, decimal? SaldoAnterior, decimal? SaldoFinal,
    bool Cortado, List<MovimentoFinanceiroResponse> Movimentos, SaldoImplantacao? Implantacao = null);

public record SaldoImplantacao(DateOnly Data, decimal Valor);

public record MovimentoRequest(
    TipoMovimentoFinanceiro Tipo,
    DateOnly Data,
    [Range(0.01, 99999999, ErrorMessage = "O valor deve ser maior que zero.")] decimal Valor,
    int ContaId,
    int CategoriaId,
    [Required(ErrorMessage = "Informe a descrição.")][StringLength(200, MinimumLength = 2, ErrorMessage = "Descrição: de 2 a 200 letras.")] string Descricao,
    [StringLength(60)] string? Documento);

public record TransferenciaRequest(
    DateOnly Data,
    [Range(0.01, 99999999, ErrorMessage = "O valor deve ser maior que zero.")] decimal Valor,
    int DeContaId,
    int ParaContaId,
    [StringLength(200)] string? Descricao);
