namespace Marketplace.Api.Models;

public class Venda
{
    public int Id { get; set; }

    public int SessaoCaixaId { get; set; }
    public SessaoCaixa? SessaoCaixa { get; set; }

    public string UsuarioId { get; set; } = "";
    public Usuario? Usuario { get; set; }

    public DateTimeOffset DataHora { get; set; } = DateTimeOffset.UtcNow;

    public decimal Subtotal { get; set; }  // soma dos itens
    public decimal Desconto { get; set; }  // em R$
    public decimal Total { get; set; }     // subtotal − desconto
    public decimal ValorPago { get; set; } // soma dos pagamentos
    public decimal Troco { get; set; }     // valor pago − total (só existe com dinheiro)

    // De onde veio a venda: PDV (pessoa), simulador ao vivo ou histórico gerado (este SEM movimentar estoque).
    public OrigemVenda Origem { get; set; } = OrigemVenda.Caixa;

    public StatusVenda Status { get; set; } = StatusVenda.Concluida;
    public DateTimeOffset? CanceladaEm { get; set; }
    public string? CanceladaPorId { get; set; }
    public Usuario? CanceladaPor { get; set; }
    public string? MotivoCancelamento { get; set; }

    public List<ItemVenda> Itens { get; set; } = [];
    public List<PagamentoVenda> Pagamentos { get; set; } = [];
}

public enum StatusVenda { Concluida, Cancelada }

public enum OrigemVenda { Caixa, Simulador, Historico }

// Item do cupom. Nome, preço e custo são COPIADOS do produto na hora da venda:
// se o preço mudar amanhã, o cupom de hoje continua mostrando o que o cliente pagou.
public class ItemVenda
{
    public int Id { get; set; }
    public int VendaId { get; set; }
    public Venda? Venda { get; set; }

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal CustoUnitario { get; set; } // para calcular o lucro depois
    public decimal Total { get; set; }
}

public class PagamentoVenda
{
    public int Id { get; set; }
    public int VendaId { get; set; }
    public Venda? Venda { get; set; }

    public FormaPagamento Forma { get; set; }
    public decimal Valor { get; set; }
}

public enum FormaPagamento { Dinheiro, Pix, Debito, Credito }
