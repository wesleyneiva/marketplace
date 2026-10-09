using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// Situacao: "Aberta", "Vencida", "VenceHoje" ou "Paga" (calculada pela data de hoje em Brasília).
public record ContaPagarResponse(
    int Id, string Descricao, string? Documento, DateOnly Vencimento, decimal Valor, string Situacao, int DiasParaVencer,
    int? FornecedorId, string? Fornecedor, int? NotaEntradaId, string? NotaNumero,
    DateOnly? PagaEm, decimal? ValorPago, string? FormaPagamento, string? PagaPor, string? Observacao);

public record ResumoContasResponse(
    int Vencidas, decimal ValorVencidas, int VencemHoje, decimal ValorHoje, int Proximos7Dias, decimal ValorProximos7Dias,
    int Abertas, decimal ValorAbertas, decimal PagoNoMes);

public record ContaPagarRequest(
    [Required(ErrorMessage = "Informe a descrição.")][StringLength(200, MinimumLength = 2, ErrorMessage = "Descrição: de 2 a 200 letras.")] string Descricao,
    [StringLength(60)] string? Documento,
    DateOnly Vencimento,
    [Range(0.01, 9999999, ErrorMessage = "O valor deve ser maior que zero.")] decimal Valor,
    int? FornecedorId,
    [StringLength(300)] string? Observacao,
    [Range(1, 36, ErrorMessage = "Repetir: de 1 a 36 meses.")] int RepetirMeses = 1);

public record PagarContaRequest(
    DateOnly PagaEm,
    [Range(0.01, 9999999, ErrorMessage = "Valor pago inválido.")] decimal ValorPago,
    [StringLength(40)] string? FormaPagamento);
