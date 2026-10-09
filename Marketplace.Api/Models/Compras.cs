namespace Marketplace.Api.Models;

public class Fornecedor : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
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
public class PedidoCompra : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

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

public class ItemPedidoCompra : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int PedidoCompraId { get; set; }
    public PedidoCompra? PedidoCompra { get; set; }

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public decimal Quantidade { get; set; }         // pedida
    public decimal CustoUnitario { get; set; }      // combinado com o fornecedor
    public decimal? QuantidadeRecebida { get; set; } // conferida na entrega (pode vir menos)
    public DateOnly? Validade { get; set; }          // informada na entrega (perecíveis)
}

// Nota fiscal (NF-e) de compra que já deu entrada no estoque. A chave de acesso é única: a mesma nota
// nunca entra duas vezes. O XML fica guardado (o comerciante precisa guardar as notas por 5 anos).
public class NotaEntrada : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Chave { get; set; } = "";       // 44 dígitos
    public string Numero { get; set; } = "";
    public string Serie { get; set; } = "";
    public DateTimeOffset DataEmissao { get; set; }
    public decimal ValorTotal { get; set; }
    public int QuantidadeItens { get; set; }

    public int FornecedorId { get; set; }
    public Fornecedor? Fornecedor { get; set; }

    // Pedido de compra que esta nota recebeu (opcional).
    public int? PedidoCompraId { get; set; }
    public PedidoCompra? PedidoCompra { get; set; }

    public string Xml { get; set; } = "";

    public DateTimeOffset RegistradaEm { get; set; } = DateTimeOffset.UtcNow;
    public string RegistradaPorId { get; set; } = "";
    public Usuario? RegistradaPor { get; set; }
}

// "O item X do fornecedor Y é o meu produto Z, e 1 embalagem dele = Fator unidades minhas."
// Criado na primeira nota (quando a pessoa escolhe o produto) e usado sozinho nas próximas.
public class VinculoFornecedorProduto : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public int FornecedorId { get; set; }
    public Fornecedor? Fornecedor { get; set; }
    public string CodigoFornecedor { get; set; } = ""; // cProd da nota

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public decimal Fator { get; set; } = 1;           // ex.: CX com 12 → 12
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
}
