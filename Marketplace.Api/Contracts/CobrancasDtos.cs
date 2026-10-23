using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// Área do dono: planos, assinaturas e mensalidades dos clientes.

public record PlanoResponse(int Id, string Nome, string? Descricao, decimal PrecoMensal, int CaixasIncluidos, decimal PrecoCaixaExtra,
    bool Ativo, int Clientes);

public record PlanoRequest(
    [Required(ErrorMessage = "Informe o nome do plano.")][StringLength(60, MinimumLength = 2)] string Nome,
    [StringLength(300)] string? Descricao,
    [Range(0, 100000, ErrorMessage = "Preço inválido.")] decimal PrecoMensal,
    [Range(1, 999, ErrorMessage = "Caixas incluídos: de 1 a 999.")] int CaixasIncluidos,
    [Range(0, 100000, ErrorMessage = "Preço do caixa extra inválido.")] decimal PrecoCaixaExtra,
    bool Ativo = true);

public record FaturaResponse(int Id, int ClienteId, string Cliente, string? Telefone, string? Responsavel, DateOnly Competencia,
    DateOnly Vencimento, decimal Valor, string Descricao, string Status, DateOnly? PagaEm, decimal? ValorPago, string? FormaPagamento,
    string? Observacao, int DiasAtraso);

public record PagarFaturaRequest(DateOnly? PagaEm, decimal? ValorPago, [Required] string Forma, [StringLength(300)] string? Observacao);

public record NovaFaturaRequest(int ClienteId, DateOnly Competencia, DateOnly? Vencimento, decimal? Valor, [StringLength(150)] string? Descricao);

public record EditarFaturaRequest(DateOnly Vencimento, [Range(0.01, 100000, ErrorMessage = "Valor inválido.")] decimal Valor,
    [StringLength(150)] string? Descricao);

public record CancelarFaturaRequest([StringLength(300)] string? Motivo);

public record ConfiguracaoCobrancaDto(
    [Range(0, 28, ErrorMessage = "Antecedência: de 0 a 28 dias.")] int DiasGerarAntes,
    bool BloqueioAutomatico,
    [Range(1, 90, ErrorMessage = "Dias até bloquear: de 1 a 90.")] int DiasParaBloquear,
    [Range(0, 28, ErrorMessage = "Aviso: de 0 a 28 dias antes.")] int DiasAvisoAntes,
    [StringLength(100)] string? ChavePix,
    [StringLength(100)] string? NomeRecebedor,
    [StringLength(20)] string? ContatoCobranca);

public record AssinaturaResponse(int? PlanoId, string? Plano, int DiaVencimento, DateOnly? TesteAte, DateOnly? CobrarDesde,
    decimal? PrecoCombinado, bool Cortesia, decimal ValorMensal, string? MotivoSuspensao, int FaturasAbertas, decimal EmAberto, int MaiorAtraso);

public record AssinaturaRequest(int? PlanoId, [Range(1, 28, ErrorMessage = "Dia de vencimento: de 1 a 28.")] int DiaVencimento,
    DateOnly? TesteAte, DateOnly? CobrarDesde, [Range(0, 100000)] decimal? PrecoCombinado, bool Cortesia);

// O que o CLIENTE vê sobre a própria mensalidade (aviso no topo do sistema, só para o Administrador dele).
public record SituacaoAssinaturaResponse(string? Nivel, string? Mensagem, List<FaturaDoCliente> Abertas, string? ChavePix,
    string? NomeRecebedor, string? Contato);

public record FaturaDoCliente(DateOnly Competencia, DateOnly Vencimento, decimal Valor, string Descricao, int DiasAtraso);

// Visão geral do dono: o faturamento DELE (as mensalidades), mês a mês.
// Meses: os últimos 24 (a tela filtra o período); Recebimentos: quanto cada cliente pagou em cada um desses meses.
public record FaturamentoPlataforma(decimal ReceitaMensal, int ClientesPagantes, decimal RecebidoMes, decimal AReceber, decimal Atrasado,
    int ClientesAtrasados, int Bloqueados, List<MesPlataforma> Meses, List<PlanoNaCarteira> PorPlano,
    List<ClienteNaCarteira> PorCliente, List<RecebidoDoCliente> Recebimentos, List<ProximaMensalidade> Proximas);

public record MesPlataforma(DateOnly Mes, decimal Faturado, decimal Recebido);

public record PlanoNaCarteira(string Plano, int Clientes, decimal Receita);

public record ClienteNaCarteira(int ClienteId, string Cliente, string? Plano, decimal Receita);

public record RecebidoDoCliente(DateOnly Mes, int ClienteId, string Cliente, decimal Valor);

// A próxima mensalidade de um cliente, que ainda não apareceu: aparece sozinha em AparecemEm.
public record ProximaMensalidade(int ClienteId, string Cliente, DateOnly Competencia, DateOnly Vencimento, decimal Valor, DateOnly AparecemEm);
