using System.Text.Json.Serialization;

namespace Marketplace.Api.Models;

// ===================== PIX AUTOMÁTICO (Mercado Pago) =====================
// Em vez do QR estático da chave da loja (o caixa confere no celular), o Marcus pede ao Mercado Pago um QR único
// para aquela venda. Quando o cliente paga, o Mercado Pago avisa o Marcus (webhook) — e o Marcus também pergunta
// a cada poucos segundos enquanto o caixa espera. O pagamento só entra na venda se o Mercado Pago disser "aprovado".

public class CobrancaPix : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Provedor { get; set; } = ProvedoresPix.MercadoPago;
    public string? IdExterno { get; set; }          // número do pagamento no Mercado Pago
    public Guid Referencia { get; set; } = Guid.NewGuid(); // vai como external_reference (confere que é desta loja)

    public decimal Valor { get; set; }
    public StatusCobrancaPix Status { get; set; } = StatusCobrancaPix.Pendente;
    public string CopiaECola { get; set; } = "";
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? PagaEm { get; set; }
    public DateTimeOffset? ConsultadaEm { get; set; } // última vez que perguntou ao Mercado Pago
    public string? StatusExterno { get; set; }        // como o Mercado Pago chama (pending, approved, cancelled…)

    public int? SessaoCaixaId { get; set; }           // o caixa que gerou (para achar um Pix pago que ficou sem venda)
    public SessaoCaixa? SessaoCaixa { get; set; }
    public string? CriadaPorId { get; set; }
    public int? VendaId { get; set; }                 // a venda em que este Pix entrou (um Pix pago entra numa venda só)
    public Venda? Venda { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<StatusCobrancaPix>))]
public enum StatusCobrancaPix { Pendente, Paga, Expirada, Cancelada }

public static class ProvedoresPix
{
    public const string MercadoPago = "MercadoPago";
}
