namespace Marketplace.Api.Models;

// TROCA: o cliente devolve itens de uma venda (dentro do prazo da loja) e eles voltam ao estoque. O valor pago por eles
// vira um CRÉDITO num vale-troca, que paga o produto novo na hora (troca de número) ou fica com o cliente para depois.
// A venda original continua concluída: na DRE a troca entra como devolução (tira da receita e devolve o custo ao CMV).
public class Troca : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public int VendaId { get; set; }          // a venda de onde os itens voltaram
    public Venda? Venda { get; set; }
    public int SessaoCaixaId { get; set; }    // o caixa onde a troca foi feita
    public SessaoCaixa? SessaoCaixa { get; set; }
    public string UsuarioId { get; set; } = "";
    public Usuario? Usuario { get; set; }
    public DateTimeOffset DataHora { get; set; } = DateTimeOffset.UtcNow;

    public string Motivo { get; set; } = "";  // MotivosTroca (vira relatório)
    public string? Observacao { get; set; }
    public decimal Valor { get; set; }        // crédito gerado (o que o cliente pagou pelos itens, já com o desconto da venda)
    public bool ForaDoPrazo { get; set; }     // passou do prazo: só com a senha de um gerente

    public int ValeTrocaId { get; set; }
    public ValeTroca? ValeTroca { get; set; }

    public List<ItemTroca> Itens { get; set; } = [];
}

public class ItemTroca : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int TrocaId { get; set; }
    public Troca? Troca { get; set; }

    public int ItemVendaId { get; set; }      // o item do cupom (não dá para devolver mais do que foi vendido)
    public ItemVenda? ItemVenda { get; set; }
    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public string Descricao { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal Valor { get; set; }        // crédito deste item
    public decimal CustoUnitario { get; set; } // o custo da venda: volta ao CMV na DRE
}

// Vale-troca: crédito da loja com código e validade. Usado como forma de pagamento (pode ser em partes: o saldo fica).
// Vencido com saldo: a loja fica com o valor (na DRE, volta como receita no dia em que venceu).
public class ValeTroca : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Codigo { get; set; } = "";  // ex.: VT-7K3M9Q (impresso no vale)
    public decimal Valor { get; set; }
    public decimal Saldo { get; set; }
    public DateOnly Validade { get; set; }    // vale até este dia (inclusive)
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public string? Cliente { get; set; }      // nome (opcional, impresso no vale)
}

public static class MotivosTroca
{
    public const string Tamanho = "Tamanho", Defeito = "Defeito", Gosto = "Gosto", Outro = "Outro";
    public static readonly string[] Todos = [Tamanho, Defeito, Gosto, Outro];
}
