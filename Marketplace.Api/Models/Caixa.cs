namespace Marketplace.Api.Models;

// Sessão (turno) de caixa: começa na ABERTURA (com o troco inicial) e termina no FECHAMENTO
// (quando o operador conta a gaveta). Toda venda pertence a uma sessão.
public class SessaoCaixa : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    // Qual caixa físico (Caixa 1, Caixa 2...). Só pode haver uma sessão aberta por caixa.
    public int NumeroCaixa { get; set; }

    public string UsuarioId { get; set; } = "";
    public Usuario? Usuario { get; set; }

    public StatusSessao Status { get; set; } = StatusSessao.Aberta;

    public DateTimeOffset AbertaEm { get; set; } = DateTimeOffset.UtcNow;
    public decimal ValorAbertura { get; set; }   // troco inicial colocado na gaveta

    public DateTimeOffset? FechadaEm { get; set; }
    public decimal? ValorEsperado { get; set; }  // quanto DEVERIA ter em dinheiro na gaveta
    public decimal? ValorContado { get; set; }   // quanto o operador contou
    public decimal? Diferenca { get; set; }      // contado − esperado (negativo = faltou dinheiro)
    public string? ObservacaoFechamento { get; set; }

    public List<Venda> Vendas { get; set; } = [];
    public List<MovimentoCaixa> Movimentos { get; set; } = [];
}

public enum StatusSessao { Aberta, Fechada }

// Dinheiro que entra ou sai da gaveta SEM ser venda.
public class MovimentoCaixa : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int SessaoCaixaId { get; set; }
    public SessaoCaixa? SessaoCaixa { get; set; }

    public TipoMovimentoCaixa Tipo { get; set; }
    public decimal Valor { get; set; }
    public string Motivo { get; set; } = "";

    public string UsuarioId { get; set; } = "";
    public Usuario? Usuario { get; set; }
    public DateTimeOffset DataHora { get; set; } = DateTimeOffset.UtcNow;
}

public enum TipoMovimentoCaixa
{
    Sangria,     // retirar dinheiro da gaveta (segurança: não acumular muito)
    Suprimento,  // colocar dinheiro (ex.: faltou troco)
}
