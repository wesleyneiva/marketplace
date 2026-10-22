using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// Ordem de serviço da oficina (Models/OrdemServico.cs). Formatos iguais a marketplace-web/src/app/core/api/ordens.api.ts.

public record OrdemResponse(
    int Id, int Numero, string Cliente, string? Telefone, string Raquete, string? Corda, string? Tensao, string Servico,
    decimal Valor, bool Paga, int? VendaId, string? Observacao, string Status, DateTimeOffset Prazo, DateTimeOffset CriadaEm,
    DateTimeOffset? ProntaEm, DateTimeOffset? EntregueEm, string? CriadaPor, bool Atrasada, bool Automatica, int? ClienteId = null,
    int? GarantiaDeNumero = null, string? MotivoGarantia = null, int Garantias = 0); // Garantias: quantas vezes esta OS voltou

public record OrdemRequest(
    [Required(ErrorMessage = "Informe o nome do cliente.")][StringLength(100, MinimumLength = 2)] string Cliente,
    [StringLength(20)] string? Telefone,
    [Required(ErrorMessage = "Descreva a raquete.")][StringLength(120, MinimumLength = 2)] string Raquete,
    [StringLength(120)] string? Corda,
    [StringLength(20)] string? Tensao,
    [Required(ErrorMessage = "Informe o serviço.")][StringLength(150, MinimumLength = 2)] string Servico,
    [Range(0, 99999)] decimal Valor,
    bool Paga,
    DateTimeOffset Prazo,
    [StringLength(300)] string? Observacao);

public record StatusOrdemRequest([Required] string Status);

public record ResumoOrdensResponse(int Recebidas, int EmAndamento, int Prontas, int Atrasadas, int EntreguesHoje,
    int RetrabalhosMes = 0, decimal CustoRetrabalhoMes = 0, int EntreguesMes = 0);

// Refazer (garantia): por quê, qual corda sai do estoque (nenhuma = só reajuste) e o prazo.
public record GarantiaRequest([Required] string Motivo, int? CordaProdutoId, DateTimeOffset Prazo, [StringLength(300)] string? Observacao);

// "Da última vez": o que este cliente (pelo telefone) usou nas últimas ordens.
public record HistoricoClienteResponse(string Cliente, string? Raquete, string? Corda, string? Tensao, DateTimeOffset Em);
