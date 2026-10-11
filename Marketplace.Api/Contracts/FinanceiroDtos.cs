using System.ComponentModel.DataAnnotations;
using Marketplace.Api.Models;

namespace Marketplace.Api.Contracts;

// ----- Contas financeiras (onde o dinheiro está) -----

public record ContaFinanceiraResponse(
    int Id, string Nome, TipoContaFinanceira Tipo, string? Banco, string? Agencia, string? Numero,
    decimal SaldoInicial, DateOnly SaldoInicialEm, decimal Saldo, int Movimentos, bool DoSistema, bool Ativa);

public record ContaFinanceiraRequest(
    [Required(ErrorMessage = "Informe o nome.")][StringLength(80, MinimumLength = 2, ErrorMessage = "Nome: de 2 a 80 letras.")] string Nome,
    TipoContaFinanceira Tipo,
    [StringLength(60)] string? Banco,
    [StringLength(10)] string? Agencia,
    [StringLength(20)] string? Numero,
    [Range(-99999999, 99999999, ErrorMessage = "Saldo inicial inválido.")] decimal SaldoInicial,
    DateOnly SaldoInicialEm);

// ----- Plano de contas -----

public record CategoriaFinanceiraResponse(
    int Id, string Codigo, string Nome, GrupoFinanceiro Grupo, string NomeGrupo, NaturezaFinanceira Natureza,
    bool DoSistema, bool Ativa, int Usos);

// Natureza só é escolhida no grupo "Fora do resultado" (empréstimo recebido × pago); nos outros vem do grupo.
public record CategoriaFinanceiraRequest(
    [Required(ErrorMessage = "Informe o nome.")][StringLength(80, MinimumLength = 2, ErrorMessage = "Nome: de 2 a 80 letras.")] string Nome,
    GrupoFinanceiro Grupo,
    NaturezaFinanceira? Natureza);

public record GrupoFinanceiroResponse(GrupoFinanceiro Grupo, int Numero, string Nome, NaturezaFinanceira? Natureza, string Explicacao);

public record SugestaoCategoriaResponse(int? CategoriaId, string? Nome);

// ----- O livro -----

public record MovimentoFinanceiroResponse(
    int Id, DateOnly Data, TipoMovimentoFinanceiro Tipo, decimal Valor, int ContaId, string Conta,
    int? CategoriaId, string? Categoria, string? CodigoCategoria, string Descricao, string? Documento,
    OrigemMovimentoFinanceiro Origem, int? ContaPagarId, Guid? TransferenciaId, bool Conciliado,
    string? CriadoPor, decimal? SaldoApos);

// Extrato: com uma conta só, vem também o saldo antes do período e o saldo linha a linha.
// Implantacao: o saldo inicial da conta, quando a data dele cai DENTRO do período (vira uma linha própria no extrato).
public record ExtratoResponse(
    DateOnly De, DateOnly Ate, decimal Entradas, decimal Saidas, decimal? SaldoAnterior, decimal? SaldoFinal,
    bool Cortado, List<MovimentoFinanceiroResponse> Movimentos, SaldoImplantacao? Implantacao = null);

public record SaldoImplantacao(DateOnly Data, decimal Valor);

public record MovimentoRequest(
    TipoMovimentoFinanceiro Tipo,
    DateOnly Data,
    [Range(0.01, 99999999, ErrorMessage = "O valor deve ser maior que zero.")] decimal Valor,
    int ContaId,
    int CategoriaId,
    [Required(ErrorMessage = "Informe a descrição.")][StringLength(200, MinimumLength = 2, ErrorMessage = "Descrição: de 2 a 200 letras.")] string Descricao,
    [StringLength(60)] string? Documento);

public record TransferenciaRequest(
    DateOnly Data,
    [Range(0.01, 99999999, ErrorMessage = "O valor deve ser maior que zero.")] decimal Valor,
    int DeContaId,
    int ParaContaId,
    [StringLength(200)] string? Descricao);

// ----- Recebíveis (maquininha) -----

public record RecebivelResponse(
    int Id, int SessaoCaixaId, int NumeroCaixa, string Forma, DateOnly DataVenda, DateOnly Previsao,
    decimal ValorBruto, decimal TaxaPercentual, decimal Taxa, decimal ValorLiquido, string Conta,
    StatusRecebivel Status, DateOnly? RecebidoEm);

// Valores LÍQUIDOS (o que cai na conta). Taxas e vendas do mês pela data da VENDA (quanto a maquininha custou neste mês).
public record ResumoRecebiveisResponse(
    decimal Hoje, decimal Amanha, decimal Proximos7Dias, decimal Proximos30Dias, decimal TotalPrevisto, int QuantidadePrevistos,
    decimal VendasEletronicasNoMes, decimal TaxasNoMes, List<PrevisaoRecebimento> ProximosDias);

public record PrevisaoRecebimento(DateOnly Data, decimal Liquido);

public record ConfiguracaoRecebimentoResponse(
    string Forma, decimal TaxaPercentual, int PrazoDias, bool DiasUteis, int ContaFinanceiraId, string Conta, DateOnly ExemploHoje);

public record ConfiguracaoRecebimentoRequest(
    [Required] string Forma,
    [Range(0, 20, ErrorMessage = "Taxa: de 0 a 20%.")] decimal TaxaPercentual,
    [Range(0, 120, ErrorMessage = "Prazo: de 0 a 120 dias.")] int PrazoDias,
    bool DiasUteis,
    int ContaFinanceiraId);

// Caixa aberto agora: ainda não entrou no Financeiro (entra no fechamento).
public record CaixaAbertoResponse(
    int SessaoId, int NumeroCaixa, string Operador, DateTimeOffset AbertaEm, int Vendas, decimal Total,
    decimal DinheiroNaGaveta, List<TotalPorForma> PorForma);

// ----- Fluxo de caixa -----

// REALIZADO: o que de fato entrou e saiu (todas as contas; transferências fora), por dia, semana ou mês.
public record FluxoRealizadoResponse(
    DateOnly De, DateOnly Ate, string Agrupar, decimal SaldoInicial, decimal Entradas, decimal Saidas, decimal SaldoFinal,
    List<PeriodoFluxo> Periodos);

public record PeriodoFluxo(DateOnly Inicio, DateOnly Fim, string Rotulo, decimal Entradas, decimal Saidas, decimal Implantacao, decimal SaldoFinal);

// PREVISTO: saldo de hoje + o que vai cair (maquininha, vendas estimadas) − o que vai vencer, dia a dia.
// Situacao do dia: "ok", "minimo" (abaixo do saldo mínimo de segurança) ou "negativo".
public record FluxoPrevistoResponse(
    DateOnly Hoje, int Dias, bool ComEstimativa, decimal SaldoHoje, decimal SaldoMinimo,
    decimal TotalRecebiveis, decimal TotalEstimado, decimal TotalSaidas, decimal SaldoFinal,
    decimal MenorSaldo, DateOnly MenorSaldoEm, DateOnly? PrimeiroNegativo, DateOnly? PrimeiroAbaixoMinimo,
    string? Alerta, int DiasDeHistoricoNaEstimativa, List<DiaFluxo> DiasPrevistos);

public record DiaFluxo(
    DateOnly Data, decimal Recebiveis, decimal Estimado, decimal Saidas, decimal SaldoFinal, string Situacao, List<ItemFluxo> Itens);

// Tipo: "conta" (a pagar), "recebivel" (maquininha), "estimativa" (vendas estimadas) ou "caixa" (caixa aberto agora).
// Valor com sinal: + entra, − sai.
public record ItemFluxo(string Tipo, string Descricao, decimal Valor, int? ContaPagarId, bool Vencida);

public record SaldoMinimoRequest([Range(0, 99999999, ErrorMessage = "Saldo mínimo inválido.")] decimal Valor);
