using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

public record EntradaRequest(
    [Range(1, int.MaxValue, ErrorMessage = "Escolha um produto.")] int ProdutoId,
    [Range(0.001, 999999, ErrorMessage = "A quantidade deve ser maior que zero.")] decimal Quantidade,
    [Range(0, 999999, ErrorMessage = "Custo inválido.")] decimal? CustoUnitario,
    DateOnly? Validade,
    [StringLength(300)] string? Observacao);

public record PerdaRequest(
    [Range(1, int.MaxValue, ErrorMessage = "Escolha um produto.")] int ProdutoId,
    [Range(0.001, 999999, ErrorMessage = "A quantidade deve ser maior que zero.")] decimal Quantidade,
    [Required(ErrorMessage = "Informe o motivo.")] string Motivo,
    int? LoteId,
    [StringLength(300)] string? Observacao);

public record AjusteRequest(
    [Range(1, int.MaxValue, ErrorMessage = "Escolha um produto.")] int ProdutoId,
    [Range(0, 999999, ErrorMessage = "Quantidade inválida.")] decimal QuantidadeContada,
    DateOnly? Validade,
    [Required(ErrorMessage = "Explique o ajuste (ex.: contagem mensal).")]
    [StringLength(300, MinimumLength = 3, ErrorMessage = "Explique o ajuste (de 3 a 300 caracteres).")]
    string Observacao);

public record MovimentacaoResponse(
    long Id,
    DateTimeOffset DataHora,
    int ProdutoId,
    string Produto,
    string Unidade,
    string Tipo,
    decimal Quantidade,
    decimal EstoqueAnterior,
    decimal EstoquePosterior,
    decimal? CustoUnitario,
    string? Motivo,
    string? Observacao,
    DateOnly? Validade,
    string Usuario,
    int? VendaId);

public record LoteResponse(
    int Id,
    int ProdutoId,
    string Produto,
    string Categoria,
    string Unidade,
    DateOnly DataValidade,
    int DiasRestantes,
    string Situacao,
    decimal QuantidadeAtual,
    decimal ValorEmRisco);

// Posição do estoque: quanto a loja tem de cada produto e quanto isso vale (custo e venda).
// Valor só conta saldo positivo (estoque negativo não "vale" dinheiro).
public record PosicaoEstoqueResponse(
    PosicaoTotais Totais,
    IReadOnlyList<PosicaoCategoria> Categorias,
    Pagina<PosicaoItem> Itens);

public record PosicaoTotais(
    int ProdutosAtivos,
    int ComSaldo,
    int Zerados,
    int Negativos,
    int AbaixoDoMinimo,
    decimal ValorCusto,
    decimal ValorVenda);

public record PosicaoCategoria(int Id, string Nome, int Produtos, decimal ValorCusto, decimal ValorVenda);

public record PosicaoItem(
    int Id,
    string Nome,
    string? CodigoBarras,
    string Categoria,
    string Unidade,
    decimal EstoqueAtual,
    decimal EstoqueMinimo,
    decimal PrecoCusto,
    decimal PrecoVenda,
    decimal ValorCusto,
    decimal ValorVenda,
    string Situacao);
