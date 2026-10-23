using System.Text.Json.Serialization;

namespace Marketplace.Api.Models;

// ===================== PIX AUTOMÁTICO (Mercado Pago ou o banco da loja) =====================
// Em vez do QR estático da chave da loja (o caixa confere no celular), o Marcus pede ao provedor um QR único
// para aquela venda. Quando o cliente paga, o Mercado Pago avisa o Marcus (webhook) — e o Marcus também pergunta
// a cada poucos segundos enquanto o caixa espera (no banco, só a pergunta). O pagamento só entra na venda se o
// provedor disser "pago".
// Provedores: Mercado Pago (API de Orders) e o banco da loja pela API Pix do Banco Central (Banco do Brasil, 22/10/2026;
// Inter, Sicoob e Itaú, 23/10/2026 — ver os perfis em Services/Pix/PixBancoCentral.cs).

public class CobrancaPix : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Provedor { get; set; } = ProvedoresPix.MercadoPago;
    public string? IdExterno { get; set; }          // número do pedido no Mercado Pago / txid no banco
    public Guid Referencia { get; set; } = Guid.NewGuid(); // vai como external_reference (confere que é desta loja); no banco, é o txid
    public string? EndToEndId { get; set; }         // banco: o número do Pix recebido (E2E), usado para devolver

    public decimal Valor { get; set; }
    public StatusCobrancaPix Status { get; set; } = StatusCobrancaPix.Pendente;
    public string CopiaECola { get; set; } = "";
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? PagaEm { get; set; }
    public DateTimeOffset? ConsultadaEm { get; set; } // última vez que perguntou ao provedor
    public string? StatusExterno { get; set; }        // situação no idioma comum (pending, approved, cancelled…)

    public int? SessaoCaixaId { get; set; }           // o caixa que gerou (para achar um Pix pago que ficou sem venda)
    public SessaoCaixa? SessaoCaixa { get; set; }
    public string? CriadaPorId { get; set; }
    public int? VendaId { get; set; }                 // a venda em que este Pix entrou (um Pix pago entra numa venda só)
    public Venda? Venda { get; set; }
    public DateTimeOffset? DevolvidaEm { get; set; }  // estorno: o provedor devolveu o dinheiro ao cliente
}

[JsonConverter(typeof(JsonStringEnumConverter<StatusCobrancaPix>))]
public enum StatusCobrancaPix { Pendente, Paga, Expirada, Cancelada, Devolvida }

public static class ProvedoresPix
{
    public const string MercadoPago = "MercadoPago";
    // API Pix do Banco Central, direto na conta da loja no banco
    public const string BancoDoBrasil = "BancoDoBrasil", Inter = "Inter", Sicoob = "Sicoob", Itau = "Itau";

    public static bool EhBanco(string? p) => p is BancoDoBrasil or Inter or Sicoob or Itau;

    // Como aparece para o caixa: "confirmado pelo Mercado Pago" / "pelo Banco do Brasil".
    public static string Nome(string? p) => p switch
    {
        MercadoPago => "Mercado Pago",
        BancoDoBrasil => "Banco do Brasil",
        Inter => "Inter",
        Sicoob => "Sicoob",
        Itau => "Itaú",
        _ => "banco",
    };
}
