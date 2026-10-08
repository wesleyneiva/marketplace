using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

public record FornecedorRequest(
    [Required(ErrorMessage = "Informe o nome.")] [StringLength(120, MinimumLength = 2, ErrorMessage = "Nome de 2 a 120 caracteres.")] string Nome,
    [StringLength(18)] string? Cnpj,
    [StringLength(100)] string? Contato,
    [StringLength(30)] string? Telefone,
    [EmailAddress(ErrorMessage = "E-mail inválido.")] [StringLength(150)] string? Email,
    [Range(1, 30, ErrorMessage = "Prazo de 1 a 30 dias.")] int PrazoEntregaDias,
    [StringLength(300)] string? Observacao);

public record FornecedorResponse(
    int Id, string Nome, string? Cnpj, string? Contato, string? Telefone, string? Email,
    int PrazoEntregaDias, string? Observacao, bool Ativo, int Produtos, int PedidosAbertos);

// ----- Sugestão de compra -----
public record ItemSugestao(
    int ProdutoId, string Produto, string Unidade, decimal EstoqueAtual, decimal EstoqueMinimo,
    decimal ConsumoDiario, decimal? DiasDeEstoque, decimal JaPedido, decimal Sugerido, decimal CustoUnitario, string Motivo);

public record SugestaoFornecedor(int FornecedorId, string Fornecedor, int PrazoEntregaDias, decimal Total, IReadOnlyList<ItemSugestao> Itens);

// ----- Pedidos -----
public record ItemPedidoRequest([Range(1, int.MaxValue)] int ProdutoId, [Range(0.001, 100000, ErrorMessage = "Quantidade inválida.")] decimal Quantidade, decimal? CustoUnitario);

public record NovoPedidoRequest(
    [Range(1, int.MaxValue, ErrorMessage = "Escolha o fornecedor.")] int FornecedorId,
    [MinLength(1, ErrorMessage = "O pedido precisa de pelo menos um item.")] List<ItemPedidoRequest> Itens,
    [StringLength(300)] string? Observacao,
    bool Enviar);

public record ItemRecebimento(int ItemId, [Range(0, 100000)] decimal QuantidadeRecebida, DateOnly? Validade, decimal? CustoUnitario);

public record ReceberPedidoRequest([MinLength(1)] List<ItemRecebimento> Itens);

public record ItemPedidoResponse(
    int Id, int ProdutoId, string Produto, string Unidade, bool Perecivel, decimal Quantidade, decimal CustoUnitario,
    decimal? QuantidadeRecebida, DateOnly? Validade, decimal Total);

public record PedidoResponse(
    int Id, int FornecedorId, string Fornecedor, string Status, bool Automatico, string? Observacao,
    DateTimeOffset CriadoEm, string CriadoPor, DateTimeOffset? EnviadoEm, DateOnly? PrevisaoEntrega,
    DateTimeOffset? RecebidoEm, string? RecebidoPor, decimal Total, bool Atrasado, IReadOnlyList<ItemPedidoResponse> Itens);
