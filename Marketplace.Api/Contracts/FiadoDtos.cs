using System.ComponentModel.DataAnnotations;
using Marketplace.Api.Models;

namespace Marketplace.Api.Contracts;

// Cliente com a situação da caderneta (saldo, vencido, atraso).
public record ClienteFiadoResponse(
    int Id, string Nome, string? Apelido, string? Telefone, string? Cpf, string? Endereco, string? Observacao,
    decimal LimiteCredito, int DiaPagamento, bool Bloqueado, bool Ativo,
    decimal Saldo, decimal Disponivel, decimal Vencido, int DiasAtraso, DateOnly? ProximoVencimento, decimal ValorProximo,
    DateTimeOffset? UltimaCompra, DateTimeOffset? UltimoPagamento);

public record ClienteRequest(
    [Required(ErrorMessage = "Informe o nome.")][StringLength(120, MinimumLength = 2, ErrorMessage = "Nome: de 2 a 120 letras.")] string Nome,
    [StringLength(60)] string? Apelido,
    [StringLength(20)] string? Telefone,
    [StringLength(14)] string? Cpf,
    [StringLength(200)] string? Endereco,
    [Range(0, 100000, ErrorMessage = "Limite: de R$ 0 a R$ 100.000.")] decimal LimiteCredito,
    [Range(1, 28, ErrorMessage = "Dia de pagamento: de 1 a 28.")] int DiaPagamento,
    bool Bloqueado,
    [StringLength(300)] string? Observacao);

public record LancamentoFiadoResponse(
    long Id, DateTimeOffset DataHora, TipoLancamentoFiado Tipo, decimal Valor, DateOnly? Vencimento, int? VendaId,
    string? Forma, bool NoCaixa, string? Observacao, string? Usuario, decimal SaldoApos, bool PodeDesfazer);

public record CadernetaResponse(ClienteFiadoResponse Cliente, List<LancamentoFiadoResponse> Lancamentos);

// Recebimento fora do caixa (ex.: PIX que caiu direto no banco): entra no Financeiro na conta escolhida.
public record PagamentoFiadoRequest(
    [Range(0.01, 1000000, ErrorMessage = "Valor inválido.")] decimal Valor,
    DateOnly Data,
    [Required] string Forma,
    int ContaFinanceiraId,
    [StringLength(200)] string? Observacao);

// Perdão / calote (só Administrador): vira despesa "Inadimplência" na DRE.
public record BaixaFiadoRequest(
    [Range(0.01, 1000000, ErrorMessage = "Valor inválido.")] decimal Valor,
    [Required(ErrorMessage = "Explique o motivo.")][StringLength(200, MinimumLength = 3, ErrorMessage = "Motivo: de 3 a 200 letras.")] string Motivo);

public record ResumoFiadoResponse(
    decimal TotalAReceber, decimal Vencido, int ClientesComSaldo, int ClientesEmAtraso, int ClientesAtrasoGrave,
    decimal VenceProximos7Dias, decimal VendidoNoMes, decimal RecebidoNoMes, int ClientesAtivos, decimal LimiteTotal);

// Busca no PDV: o mínimo para o operador decidir (telefone só os 4 últimos dígitos).
public record ClientePdvResponse(
    int Id, string Nome, string? Apelido, string? TelefoneFinal, decimal Saldo, decimal LimiteCredito, decimal Disponivel,
    decimal Vencido, int DiasAtraso, bool Bloqueado, string? Restricao);

public record ReceberNoCaixaRequest(
    int ClienteId,
    [Range(0.01, 1000000, ErrorMessage = "Valor inválido.")] decimal Valor,
    [Required] string Forma);

public record ReciboFiadoResponse(string Cliente, decimal Valor, string Forma, decimal SaldoAnterior, decimal SaldoAtual, DateTimeOffset DataHora, string Operador);
