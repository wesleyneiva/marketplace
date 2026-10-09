namespace Marketplace.Api.Contracts;

// Prévia da importação de produtos: o que ACONTECERIA com cada linha (nada é gravado).
public record PreviaImportacaoResponse(
    string Arquivo,
    int TotalLinhas,
    int Novos,
    int Atualizados,
    int ComErro,
    int ComAviso,
    List<string> CategoriasNovas,
    List<string> ErrosGerais,   // problema no arquivo inteiro (ex.: falta a coluna "Nome")
    List<string> AvisosGerais,  // ex.: coluna desconhecida ignorada
    List<LinhaPreviaResponse> Linhas);

// Acao: "Novo", "Atualizar" ou "Erro". Os valores vêm já interpretados (null = célula vazia).
public record LinhaPreviaResponse(
    int Linha,
    string Acao,
    string? CodigoBarras,
    string? Nome,
    string? Categoria,
    string? Unidade,
    decimal? PrecoCusto,
    decimal? PrecoVenda,
    decimal? EstoqueMinimo,
    bool? ControlaValidade,
    decimal? EstoqueInicial,
    DateOnly? Validade,
    List<string> Erros,
    List<string> Avisos);
