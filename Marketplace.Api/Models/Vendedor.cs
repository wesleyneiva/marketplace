namespace Marketplace.Api.Models;

// Vendedor: quem atendeu o cliente (pode não ser quem está no caixa e não precisa de login). Escolhido na venda;
// a comissão do mês = % do vendedor (ou o % da categoria do produto, quando ela tem um próprio) sobre o que ele vendeu,
// menos o que voltou em trocas. O fechamento do mês vira uma conta a pagar ("Comissões de vendedores").
public class Vendedor : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Nome { get; set; } = "";
    public decimal ComissaoPercentual { get; set; }  // ex.: 3 = 3%
    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

// Comissão de um mês já lançada em Contas a pagar (uma por vendedor e mês: não lança duas vezes).
public class ComissaoMes : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public int VendedorId { get; set; }
    public Vendedor? Vendedor { get; set; }
    public DateOnly Competencia { get; set; }        // dia 1 do mês
    public decimal Valor { get; set; }
    public int ContaPagarId { get; set; }
    public ContaPagar? ContaPagar { get; set; }
    public DateTimeOffset LancadaEm { get; set; } = DateTimeOffset.UtcNow;
}
