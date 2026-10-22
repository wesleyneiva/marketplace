using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// ----- Troca no caixa -----
// A venda achada (pelo número do cupom ou pelo produto devolvido), com o que ainda dá para devolver de cada item.
public record ItemParaTroca(int ItemVendaId, int ProdutoId, string Descricao, string Unidade, decimal Quantidade,
    decimal JaDevolvido, decimal Disponivel, decimal CreditoUnitario); // crédito por unidade (já com o desconto da venda)

public record VendaParaTrocaResponse(int VendaId, DateTimeOffset DataHora, int NumeroCaixa, decimal Total, int Dias,
    int PrazoDias, bool DentroDoPrazo, string? Bloqueio, List<ItemParaTroca> Itens);

// Vendas recentes com um produto (o cliente veio sem o cupom: bipa o que está devolvendo).
public record VendaComProdutoResponse(int VendaId, DateTimeOffset DataHora, int NumeroCaixa, decimal Total, string Itens, bool DentroDoPrazo);

public record ItemTrocaRequest(int ItemVendaId, [Range(0.001, 100000, ErrorMessage = "Quantidade inválida.")] decimal Quantidade);

public record NovaTrocaRequest(
    int VendaId,
    [MinLength(1, ErrorMessage = "Marque o que o cliente está devolvendo.")] List<ItemTrocaRequest> Itens,
    [Required] string Motivo,
    [StringLength(200)] string? Observacao,
    [StringLength(100)] string? Cliente,
    AutorizacaoRequest? Autorizacao = null); // fora do prazo: a senha de um gerente

public record ValeTrocaResponse(int Id, string Codigo, decimal Valor, decimal Saldo, DateOnly Validade, string? Cliente,
    string Situacao, DateTimeOffset CriadoEm, int? TrocaId, int? VendaId); // Situacao: Disponivel, Usado, Vencido

public record ItemTrocaResponse(int ProdutoId, string Descricao, decimal Quantidade, decimal Valor);

public record TrocaResponse(int Id, int VendaId, DateTimeOffset DataHora, int NumeroCaixa, string Operador, string Motivo,
    string? Observacao, decimal Valor, bool ForaDoPrazo, ValeTrocaResponse Vale, List<ItemTrocaResponse> Itens);

// ----- Relatório (Vendas → Trocas e vales) -----
public record TrocasPorMotivo(string Motivo, int Quantidade, decimal Valor);
public record ProdutoTrocado(string Produto, decimal Quantidade, decimal Valor, int Trocas);

public record TrocasRelatorioResponse(DateOnly De, DateOnly Ate, int Quantidade, decimal Valor, List<TrocasPorMotivo> PorMotivo,
    List<ProdutoTrocado> Produtos, List<TrocaResponse> Trocas,
    int ValesEmAberto, decimal SaldoEmAberto, decimal VencidosNoPeriodo, List<ValeTrocaResponse> Vales);

public record ConfiguracaoTrocasRequest([Range(1, 365, ErrorMessage = "Prazo de troca de 1 a 365 dias.")] int PrazoTrocaDias,
    [Range(7, 730, ErrorMessage = "Validade do vale de 7 a 730 dias.")] int ValidadeValeDias);
