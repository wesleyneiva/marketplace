using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// O que a tela manda para criar/editar um produto.
// As [anotações] validam sozinhas: se algo estiver errado, a API responde 400 com a lista de erros.
public record ProdutoRequest(
    [StringLength(14, MinimumLength = 8, ErrorMessage = "O código de barras deve ter de 8 a 14 dígitos.")]
    [RegularExpression(@"^\d+$", ErrorMessage = "O código de barras só pode ter números.")]
    string? CodigoBarras,

    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "O nome deve ter de 2 a 150 caracteres.")]
    string Nome,

    [Range(1, int.MaxValue, ErrorMessage = "Escolha uma categoria.")]
    int CategoriaId,

    [Required(ErrorMessage = "Informe a unidade.")]
    string Unidade,

    [Range(0, 999999, ErrorMessage = "Preço de custo inválido.")]
    decimal PrecoCusto,

    [Range(0.01, 999999, ErrorMessage = "O preço de venda deve ser maior que zero.")]
    decimal PrecoVenda,

    [Range(0, 999999, ErrorMessage = "Estoque mínimo inválido.")]
    decimal EstoqueMinimo,

    // O estoque NÃO vem no cadastro: começa em zero e só muda por movimentação (entrada, perda...).
    bool ControlaValidade,

    // Fornecedor principal (opcional): de quem a sugestão de compra pede este produto.
    int? FornecedorId = null,

    // Código na balança (PLU), para a etiqueta do açougue/hortifrúti. Opcional.
    [Range(1, 99999, ErrorMessage = "Código da balança: de 1 a 99999.")]
    int? CodigoBalanca = null,

    // Dados fiscais (opcionais por enquanto; obrigatórios quando a loja emitir NFC-e).
    [RegularExpression(@"^\d{8}$", ErrorMessage = "NCM: 8 dígitos.")] string? Ncm = null,
    [RegularExpression(@"^\d{7}$", ErrorMessage = "CEST: 7 dígitos.")] string? Cest = null,
    [RegularExpression(@"^\d{4}$", ErrorMessage = "CFOP: 4 dígitos.")] string? Cfop = null,
    [Range(0, 8, ErrorMessage = "Origem: de 0 a 8.")] int Origem = 0,
    [RegularExpression(@"^\d{2,3}$", ErrorMessage = "CSOSN/CST: 2 ou 3 dígitos.")] string? SituacaoTributaria = null);

// O que a API devolve: já com o nome da categoria e a margem calculada.
public record ProdutoResponse(
    int Id,
    string? CodigoBarras,
    string Nome,
    int CategoriaId,
    string Categoria,
    string Unidade,
    decimal PrecoCusto,
    decimal PrecoVenda,
    decimal MargemPercentual,
    decimal EstoqueAtual,
    decimal EstoqueMinimo,
    bool EstoqueBaixo,
    bool ControlaValidade,
    bool Ativo,
    int? FornecedorId,
    string? Fornecedor,
    int? CodigoBalanca,
    string? Ncm,
    string? Cest,
    string Cfop,
    int Origem,
    string? SituacaoTributaria);

// Resultado paginado: uma "página" de itens + o total (para a tela montar a paginação).
public record Pagina<T>(IReadOnlyList<T> Itens, int Total, int PaginaAtual, int TamanhoPagina);

public record CategoriaRequest(
    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "O nome deve ter de 2 a 60 caracteres.")]
    string Nome);

public record CategoriaResponse(int Id, string Nome, bool Ativa, int QuantidadeProdutos);

// Etiqueta de gôndola (preço na prateleira).
public record EtiquetaResponse(
    int Id, string Nome, string? CodigoBarras, string Unidade, decimal PrecoVenda, string Categoria, DateTimeOffset? PrecoAlteradoEm);

// Etiqueta da balança lida no PDV: o produto e a quantidade (o peso; se a etiqueta traz o preço, o peso é calculado).
public record EtiquetaBalancaResponse(ProdutoResponse Produto, decimal Quantidade, decimal? PrecoEtiqueta, decimal? PesoEtiqueta);

// Configuração da empresa usada pelas telas (por enquanto, o formato da etiqueta da balança).
public record ConfiguracaoEmpresaResponse(int BalancaDigitosCodigo, string BalancaEtiqueta, string ExemploEtiqueta);

public record ConfiguracaoEmpresaRequest(
    [Range(4, 5, ErrorMessage = "O código do produto na etiqueta tem 4 ou 5 dígitos.")] int BalancaDigitosCodigo,
    [Required] string BalancaEtiqueta);
