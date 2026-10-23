namespace Marketplace.Api.Models;

// ===== Assinaturas do SaaS (área do dono da plataforma) =====
// Estas tabelas são da PLATAFORMA, não de um mercado: NÃO têm a etiqueta IDaEmpresa (sem filtro por empresa).
// Só a área do dono lê e grava (e o cliente lê a própria situação em /api/empresa/assinatura).

// Plano que o dono vende. Mensalidade = preço + (caixas além dos incluídos) × preço do caixa extra.
public class Plano
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Descricao { get; set; }
    public decimal PrecoMensal { get; set; }
    public int CaixasIncluidos { get; set; } = 1;
    public decimal PrecoCaixaExtra { get; set; }
    public bool Ativo { get; set; } = true; // inativo: não aparece para clientes novos (os antigos continuam nele)
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

// Uma mensalidade de um cliente. Gerada sozinha alguns dias antes do vencimento (ou à mão pelo dono).
public class Fatura
{
    public int Id { get; set; }
    public int ClienteId { get; set; }          // a Empresa (mercado) que paga
    public Empresa? Cliente { get; set; }
    public DateOnly Competencia { get; set; }   // o mês cobrado (sempre dia 1º)
    public DateOnly Vencimento { get; set; }
    public decimal Valor { get; set; }
    public string Descricao { get; set; } = ""; // ex.: "Plano Básico · 2 caixas · out/2026"
    public string Status { get; set; } = StatusFatura.Aberta;
    public DateOnly? PagaEm { get; set; }
    public decimal? ValorPago { get; set; }
    public string? FormaPagamento { get; set; } // Pix, Boleto, Dinheiro, Transferencia, Cartao
    public string? Observacao { get; set; }
    public bool PagaPelaLoja { get; set; }
    public bool Mensalidade { get; set; } = true; // false = cobrança à parte (ex.: implantação): não conta como "o mês já tem fatura"      // paga pela baixa da loja no Contas a pagar (desfazer lá reabre aqui)
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
}

public static class StatusFatura
{
    public const string Aberta = "Aberta";
    public const string Paga = "Paga";
    public const string Cancelada = "Cancelada";
}

public static class FormasRecebimento
{
    public static readonly string[] Todas = ["Pix", "Boleto", "Transferencia", "Dinheiro", "Cartao"];
}

// Regras de cobrança da plataforma (uma linha só, Id = 1).
public class ConfiguracaoPlataforma
{
    public int Id { get; set; } = 1;
    public int DiasGerarAntes { get; set; } = 10;    // a mensalidade aparece X dias antes do vencimento
    public bool BloqueioAutomatico { get; set; } = true;
    public int DiasParaBloquear { get; set; } = 10;  // dias de atraso até o bloqueio automático
    public int DiasAvisoAntes { get; set; } = 5;     // o cliente vê o aviso X dias antes de vencer
    public string? ChavePix { get; set; }            // onde o cliente paga (aparece no aviso e na mensagem)
    public string? NomeRecebedor { get; set; }
    public string? ContatoCobranca { get; set; }     // WhatsApp do dono para o cliente falar
}

// Por que o cliente está suspenso.
public static class MotivosSuspensao
{
    public const string Atraso = "Atraso";   // bloqueio automático: volta sozinho quando pagar
    public const string Manual = "Manual";   // o dono suspendeu
}
