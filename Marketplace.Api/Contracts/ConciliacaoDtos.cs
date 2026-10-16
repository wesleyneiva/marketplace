using Marketplace.Api.Models;

namespace Marketplace.Api.Contracts;

// Conciliação bancária (extrato OFX). Formatos iguais a core/api/conciliacao.api.ts.

public record ImportacaoExtratoResponse(
    int ExtratoId, int ContaId, string Conta, string? Banco, DateOnly De, DateOnly Ate,
    int Linhas, int Novas, int Repetidas, int Sugestoes, string? Aviso);

// Uma linha do extrato, com a sugestão do sistema (se achou).
public record LinhaExtratoResponse(
    int Id, DateOnly Data, decimal Valor, string Descricao, string? Documento,
    StatusLinhaExtrato Status, Guid? ConciliacaoId, SugestaoConciliacaoResponse? Sugestao, int? CategoriaSugeridaId);

public record MovimentoResumo(int Id, DateOnly Data, TipoMovimentoFinanceiro Tipo, decimal Valor, string Descricao, string? Categoria, OrigemMovimentoFinanceiro Origem);

// "Estas linhas do extrato são estes lançamentos do sistema."
public record SugestaoConciliacaoResponse(
    string Tipo,             // "exata" (mesmo valor) · "agrupada" (várias linhas do banco = um lançamento: o PIX do dia)
    List<int> LinhaIds, List<int> MovimentoIds, List<MovimentoResumo> Movimentos, string Explicacao);

public record ConciliacaoGrupoResponse(Guid Id, DateTimeOffset? Em, List<LinhaExtratoResponse> Linhas, List<MovimentoResumo> Movimentos);

public record ContaConciliacaoResponse(int Id, string Nome, string? Banco, int Pendentes, DateOnly? UltimoExtratoAte);

public record ConciliacaoResponse(
    List<ContaConciliacaoResponse> Contas, int? ContaId,
    decimal? SaldoBanco, DateOnly? SaldoBancoEm, decimal? SaldoSistema,   // o banco diz × o sistema diz, na mesma data
    DateOnly? De, DateOnly? Ate,                                         // período coberto pelos extratos importados
    int Conciliadas, int Ignoradas,
    List<LinhaExtratoResponse> Pendentes,
    List<MovimentoResumo> SemExtrato,                                    // no sistema, mas não apareceu no banco
    List<ConciliacaoGrupoResponse> Recentes,
    List<LinhaExtratoResponse> IgnoradasLista);

public record ConciliarRequest(List<int> LinhaIds, List<int> MovimentoIds);
public record AceitarSugestoesRequest(int ContaId);
public record LancarLinhaRequest(int CategoriaId, string? Descricao);
