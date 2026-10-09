using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// ----- Caixa -----
public record AbrirCaixaRequest(
    [Range(1, 999, ErrorMessage = "Número do caixa inválido.")] int NumeroCaixa, // o limite real é o da empresa
    [Range(0, 10000, ErrorMessage = "Troco inicial inválido.")] decimal ValorAbertura);

public record MovimentoCaixaRequest(
    [Range(0.01, 100000, ErrorMessage = "Informe um valor maior que zero.")] decimal Valor,
    [Required(ErrorMessage = "Informe o motivo.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Motivo de 3 a 150 caracteres.")] string Motivo);

public record FecharCaixaRequest(
    [Range(0, 1000000, ErrorMessage = "Valor contado inválido.")] decimal ValorContado,
    [StringLength(300)] string? Observacao);

// Os caixas da empresa (1 até o limite do plano) e quem está em cada um agora.
public record CaixaDisponivel(int Numero, string? OcupadoPor);

public record TotalPorForma(string Forma, decimal Valor);

public record ResumoCaixaResponse(
    int SessaoId,
    int NumeroCaixa,
    string Operador,
    string Status,
    DateTimeOffset AbertaEm,
    DateTimeOffset? FechadaEm,
    decimal ValorAbertura,
    int QuantidadeVendas,
    decimal TotalVendido,
    decimal TicketMedio,
    IReadOnlyList<TotalPorForma> PorForma,
    decimal TrocoDado,
    decimal Sangrias,
    decimal Suprimentos,
    decimal DinheiroEsperado,
    int VendasCanceladas,
    decimal ValorCancelado,
    decimal? ValorContado,
    decimal? Diferenca,
    string? ObservacaoFechamento);

// ----- Venda -----
public record ItemVendaRequest(
    [Range(1, int.MaxValue)] int ProdutoId,
    [Range(0.001, 100000, ErrorMessage = "Quantidade inválida.")] decimal Quantidade);

public record PagamentoRequest(
    [Required] string Forma,
    [Range(0.01, 1000000, ErrorMessage = "Valor de pagamento inválido.")] decimal Valor);

public record NovaVendaRequest(
    [MinLength(1, ErrorMessage = "A venda precisa de pelo menos um item.")] List<ItemVendaRequest> Itens,
    [Range(0, 1000000, ErrorMessage = "Desconto inválido.")] decimal Desconto,
    [MinLength(1, ErrorMessage = "Informe a forma de pagamento.")] List<PagamentoRequest> Pagamentos);

public record CancelarVendaRequest(
    [Required(ErrorMessage = "Informe o motivo.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Motivo de 3 a 200 caracteres.")] string Motivo);

public record ItemVendaResponse(int ProdutoId, string Descricao, string Unidade, decimal Quantidade, decimal PrecoUnitario, decimal Total);

public record PagamentoResponse(string Forma, decimal Valor);

public record VendaResponse(
    int Id,
    DateTimeOffset DataHora,
    int NumeroCaixa,
    string Operador,
    string Status,
    decimal Subtotal,
    decimal Desconto,
    decimal Total,
    decimal ValorPago,
    decimal Troco,
    IReadOnlyList<ItemVendaResponse> Itens,
    IReadOnlyList<PagamentoResponse> Pagamentos,
    string? MotivoCancelamento);

public record VendaListaResponse(int Id, DateTimeOffset DataHora, int NumeroCaixa, string Operador, string Status, int QuantidadeItens, decimal Total, string Formas);

public record VendasPorHora(int Hora, int Quantidade, decimal Total);

public record ResumoVendasResponse(
    DateOnly Data,
    int QuantidadeVendas,
    decimal TotalVendido,
    decimal TicketMedio,
    decimal LucroBruto,
    IReadOnlyList<TotalPorForma> PorForma,
    IReadOnlyList<VendasPorHora> PorHora);
