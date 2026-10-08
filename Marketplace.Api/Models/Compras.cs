namespace Marketplace.Api.Models;

public class Fornecedor
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Cnpj { get; set; }
    public string? Contato { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }

    // Quantos dias o fornecedor leva para entregar depois do pedido (dias corridos).
    public int PrazoEntregaDias { get; set; } = 2;
    public string? Observacao { get; set; }
    public bool Ativo { get; set; } = true;

    public List<Produto> Produtos { get; set; } = [];
}

// Pedido de compra: Rascunho → Enviado (ao fornecedor) → Recebido (entra no estoque). Ou Cancelado.
public class PedidoCompra
{
    public int Id { get; set; }

    public int FornecedorId { get; set; }
    public Fornecedor? Fornecedor { get; set; }

    public StatusPedido Status { get; set; } = StatusPedido.Rascunho;
    public bool Automatico { get; set; } // feito pelo simulador ("gerente automático")
    public string? Observacao { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public string CriadoPorId { get; set; } = "";
    public Usuario? CriadoPor { get; set; }

    public DateTimeOffset? EnviadoEm { get; set; }
    public DateOnly? PrevisaoEntrega { get; set; }

    public DateTimeOffset? RecebidoEm { get; set; }
    public string? RecebidoPorId { get; set; }
    public Usuario? RecebidoPor { get; set; }

    public List<ItemPedidoCompra> Itens { get; set; } = [];
}

public enum StatusPedido { Rascunho, Enviado, Recebido, Cancelado }

public class ItemPedidoCompra
{
    public int Id { get; set; }
    public int PedidoCompraId { get; set; }
    public PedidoCompra? PedidoCompra { get; set; }

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public decimal Quantidade { get; set; }         // pedida
    public decimal CustoUnitario { get; set; }      // combinado com o fornecedor
    public decimal? QuantidadeRecebida { get; set; } // conferida na entrega (pode vir menos)
    public DateOnly? Validade { get; set; }          // informada na entrega (perecíveis)
}
