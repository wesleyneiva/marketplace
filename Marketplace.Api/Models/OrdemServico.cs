namespace Marketplace.Api.Models;

// Ordem de serviço da oficina (loja de tênis: encordoamento, troca de grip). O cliente deixa a raquete, a loja faz e
// avisa; ele busca. A COBRANÇA continua no caixa (o serviço e a corda são produtos comuns, o serviço sem estoque):
// a OS guarda o que fazer, o prazo, a situação e se já foi paga (VendaId = a venda do caixa, quando ligada).
public class OrdemServico : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int Numero { get; set; }                 // sequência da loja (OS nº 1, 2, 3…)

    public string Cliente { get; set; } = "";
    public string? Telefone { get; set; }           // WhatsApp, só números com DDD (para avisar que ficou pronta)
    public string Raquete { get; set; } = "";       // "Babolat Pure Aero (a azul)"
    public string? Corda { get; set; }              // "Babolat RPM Blast 1.25" ou "corda do cliente"
    public string? Tensao { get; set; }             // "24/22 lb"
    public string Servico { get; set; } = "";       // "Encordoamento (mão de obra)"
    public decimal Valor { get; set; }              // serviço + corda
    public bool Paga { get; set; }
    public int? VendaId { get; set; }
    public string? Observacao { get; set; }

    public string Status { get; set; } = StatusOrdem.Recebida;
    public DateTimeOffset Prazo { get; set; }
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProntaEm { get; set; }
    public DateTimeOffset? EntregueEm { get; set; }
    public string? CriadaPorId { get; set; }
    public bool Automatica { get; set; }            // criada pelo bot (loja em teste)
}

public static class StatusOrdem
{
    public const string Recebida = "Recebida";
    public const string EmAndamento = "EmAndamento";
    public const string Pronta = "Pronta";
    public const string Entregue = "Entregue";
    public const string Cancelada = "Cancelada";

    public static readonly string[] Todos = [Recebida, EmAndamento, Pronta, Entregue, Cancelada];
    public static bool EmAberto(string s) => s is Recebida or EmAndamento;
}
