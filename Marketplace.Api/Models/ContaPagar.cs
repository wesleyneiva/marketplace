namespace Marketplace.Api.Models;

// Conta a pagar: parcela de uma nota de fornecedor (as duplicatas do boleto) ou lançada à mão (aluguel, luz...).
public class ContaPagar : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Descricao { get; set; } = "";
    public string? Documento { get; set; }          // nº da duplicata / boleto
    public DateOnly Vencimento { get; set; }
    public decimal Valor { get; set; }

    public int? FornecedorId { get; set; }
    public Fornecedor? Fornecedor { get; set; }

    // Veio de uma NF-e (as parcelas da nota)?
    public int? NotaEntradaId { get; set; }
    public NotaEntrada? NotaEntrada { get; set; }

    // Pagamento (nulo = em aberto).
    public DateOnly? PagaEm { get; set; }
    public decimal? ValorPago { get; set; }         // pode ser diferente (juros, desconto)
    public string? FormaPagamento { get; set; }
    public string? PagaPorId { get; set; }
    public Usuario? PagaPor { get; set; }

    public string? Observacao { get; set; }
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public string? CriadaPorId { get; set; }
    public Usuario? CriadaPor { get; set; }
}
