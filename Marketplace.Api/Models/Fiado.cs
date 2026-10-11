using System.Text.Json.Serialization;

namespace Marketplace.Api.Models;

// ===================== FIADO (a caderneta) =====================
// Cliente da LOJA (o consumidor), não confundir com a Empresa (o mercado que assina o sistema).
// A caderneta é um extrato: compras − pagamentos − estornos − baixas = saldo devedor. Nada se apaga: erro se corrige
// com outro lançamento (como no livro financeiro).

public class Cliente : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Nome { get; set; } = "";
    public string? Apelido { get; set; }          // "Dona Maria do 302": é assim que o caixa procura
    public string? Telefone { get; set; }         // WhatsApp (só dígitos, com DDD)
    public string? Cpf { get; set; }              // opcional (LGPD: só o necessário)
    public string? Endereco { get; set; }

    public decimal LimiteCredito { get; set; }    // 0 = não compra fiado (só gerente libera)
    public int DiaPagamento { get; set; } = 10;   // o que comprou no mês vence neste dia do mês seguinte
    public bool Bloqueado { get; set; }           // bloqueado pelo dono: só gerente vende fiado
    public bool Ativo { get; set; } = true;
    public string? Observacao { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public string? CriadoPorId { get; set; }
}

public class LancamentoFiado : IDaEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }

    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public DateTimeOffset DataHora { get; set; } = DateTimeOffset.UtcNow;
    public TipoLancamentoFiado Tipo { get; set; }
    public decimal Valor { get; set; }            // sempre POSITIVO; o Tipo diz se aumenta ou diminui a dívida

    public DateOnly? Vencimento { get; set; }     // compras: quando vence (dia de pagamento do cliente)

    // De onde veio
    public int? VendaId { get; set; }             // compra (ou o estorno dela)
    public Venda? Venda { get; set; }
    public int? SessaoCaixaId { get; set; }       // pagamento feito no caixa (PDV): entra no fechamento
    public SessaoCaixa? SessaoCaixa { get; set; }
    public FormaPagamento? Forma { get; set; }    // pagamento: como o cliente pagou
    public int? MovimentoFinanceiroId { get; set; } // pagamento fora do PDV: a entrada no livro
    public MovimentoFinanceiro? MovimentoFinanceiro { get; set; }

    public string? Observacao { get; set; }
    public string? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TipoLancamentoFiado>))]
public enum TipoLancamentoFiado
{
    Compra,     // venda no fiado (aumenta a dívida)
    Pagamento,  // o cliente pagou (no caixa ou fora dele)
    Estorno,    // a venda foi cancelada
    Baixa,      // perdão / calote: vira despesa (Inadimplência) na DRE
}
