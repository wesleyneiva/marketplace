using System.Text.Json.Serialization;

namespace Marketplace.Api.Models;

// ===================== CONCILIAÇÃO BANCÁRIA (extrato OFX) =====================
// O cliente baixa o extrato no internet banking (arquivo .ofx, que todo banco gera: Bradesco, Caixa, BB, Itaú…)
// e manda no sistema. Cada linha do extrato é conferida com o livro (MovimentosFinanceiros):
//   - 1 linha = 1 lançamento (boleto pago, transferência);
//   - várias linhas = 1 lançamento (o banco mostra cada PIX; o sistema guarda o PIX do dia somado);
//   - 1 linha = vários lançamentos (a maquininha deposita o líquido; o sistema tem bruto + taxa).
// Por isso a ligação é um código de conciliação (Guid) que as linhas e os lançamentos conferidos juntos compartilham.

// Um arquivo importado (para saber de onde veio cada linha e o saldo que o banco informou).
public class ExtratoBancario : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public int ContaFinanceiraId { get; set; }
    public ContaFinanceira? ContaFinanceira { get; set; }

    public string NomeArquivo { get; set; } = "";
    public string? Banco { get; set; }          // código do banco no OFX (237 = Bradesco, 104 = Caixa…)
    public string? Agencia { get; set; }
    public string? NumeroConta { get; set; }
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    public decimal? SaldoFinal { get; set; }    // o saldo que o banco informou no arquivo
    public DateOnly? SaldoEm { get; set; }

    public int Linhas { get; set; }             // quantas transações o arquivo tinha
    public int Novas { get; set; }              // quantas entraram (as outras já tinham vindo em outro arquivo)

    public DateTimeOffset ImportadoEm { get; set; } = DateTimeOffset.UtcNow;
    public string? ImportadoPorId { get; set; }
    public Usuario? ImportadoPor { get; set; }
}

// Uma linha do extrato do banco.
public class LinhaExtrato : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public int ExtratoBancarioId { get; set; }
    public ExtratoBancario? ExtratoBancario { get; set; }
    public int ContaFinanceiraId { get; set; }

    public DateOnly Data { get; set; }
    public decimal Valor { get; set; }          // com sinal, como no banco: + entrou, − saiu
    public string Descricao { get; set; } = "";
    public string? Documento { get; set; }
    public string? TipoBanco { get; set; }      // TRNTYPE do OFX (CREDIT, DEBIT, PAYMENT, XFER…)

    // Identidade da linha para não importar duas vezes: o FITID do banco (ou, sem ele, data + valor + descrição).
    public string Chave { get; set; } = "";

    public StatusLinhaExtrato Status { get; set; } = StatusLinhaExtrato.Pendente;
    public Guid? ConciliacaoId { get; set; }    // igual ao dos lançamentos conferidos com ela
    public DateTimeOffset? ConciliadaEm { get; set; }
    public string? ConciliadaPorId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<StatusLinhaExtrato>))]
public enum StatusLinhaExtrato
{
    Pendente,    // ainda não conferida
    Conciliada,  // achou o lançamento no sistema (ou foi lançada a partir do extrato)
    Ignorada,    // o cliente disse que não precisa conferir (ex.: aplicação automática que ele não controla)
}
