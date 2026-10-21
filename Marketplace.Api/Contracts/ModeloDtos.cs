using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// Grade de tamanho e cor (Models/Modelo.cs). Formatos iguais a marketplace-web/src/app/core/api/modelos.api.ts.

public record ModeloResumoResponse(
    int Id, string Nome, string? Referencia, string? Marca, int CategoriaId, string Categoria, string? Fornecedor,
    decimal PrecoMinimo, decimal PrecoMaximo, int Variacoes, decimal EstoqueTotal, int VariacoesZeradas, bool Ativo,
    string[] Tamanhos, string[] Cores);

public record VariacaoResponse(
    int ProdutoId, string Nome, string Tamanho, string Cor, string? CodigoBarras, decimal PrecoCusto, decimal PrecoVenda,
    decimal EstoqueAtual, decimal EstoqueMinimo, bool Ativo);

public record ModeloDetalheResponse(
    int Id, string Nome, string? Referencia, string? Marca, int CategoriaId, string Categoria, int? FornecedorId, string? Fornecedor,
    string Unidade, string? Ncm, string[] Tamanhos, string[] Cores, bool Ativo, List<VariacaoResponse> Variacoes);

public record CombinacaoRequest(
    [Required][StringLength(20)] string Tamanho,
    [Required][StringLength(40)] string Cor,
    [StringLength(14, MinimumLength = 8, ErrorMessage = "O código de barras deve ter de 8 a 14 dígitos.")]
    [RegularExpression(@"^\d+$", ErrorMessage = "O código de barras só pode ter números.")] string? CodigoBarras = null);

// Criar: os tamanhos e as cores da matriz + quais combinações a loja trabalha (vazio = todas).
// Código de barras vazio = o sistema gera um código interno (EAN-13 começando com 20) para imprimir na etiqueta.
public record NovoModeloRequest(
    [Required(ErrorMessage = "Informe o nome do modelo.")][StringLength(120, MinimumLength = 2)] string Nome,
    [StringLength(40)] string? Referencia,
    [StringLength(60)] string? Marca,
    [Range(1, int.MaxValue, ErrorMessage = "Escolha uma categoria.")] int CategoriaId,
    int? FornecedorId,
    [Required] string Unidade,
    [Range(0, 999999)] decimal PrecoCusto,
    [Range(0.01, 999999, ErrorMessage = "O preço de venda deve ser maior que zero.")] decimal PrecoVenda,
    [Range(0, 999999)] decimal EstoqueMinimo,
    [RegularExpression(@"^\d{8}$", ErrorMessage = "NCM: 8 dígitos.")] string? Ncm,
    [MinLength(1, ErrorMessage = "Informe pelo menos um tamanho.")] string[] Tamanhos,
    [MinLength(1, ErrorMessage = "Informe pelo menos uma cor.")] string[] Cores,
    List<CombinacaoRequest>? Combinacoes = null);

// Editar: vale para todas as variações (nome, categoria, fornecedor). Preços e mínimo só quando "aplicar" vier marcado
// (cada variação pode ter o seu preço, mudado pelo cadastro do produto).
public record EditarModeloRequest(
    [Required(ErrorMessage = "Informe o nome do modelo.")][StringLength(120, MinimumLength = 2)] string Nome,
    [StringLength(40)] string? Referencia,
    [StringLength(60)] string? Marca,
    [Range(1, int.MaxValue, ErrorMessage = "Escolha uma categoria.")] int CategoriaId,
    int? FornecedorId,
    [RegularExpression(@"^\d{8}$", ErrorMessage = "NCM: 8 dígitos.")] string? Ncm,
    bool AplicarPrecos,
    [Range(0, 999999)] decimal PrecoCusto,
    [Range(0.01, 999999)] decimal PrecoVenda,
    [Range(0, 999999)] decimal EstoqueMinimo);

// Acrescentar tamanhos/cores/combinações a um modelo que já existe (só cria as que faltam).
public record NovasVariacoesRequest([MinLength(1)] List<CombinacaoRequest> Combinacoes);

// Entrada de mercadoria em grade (ex.: chegou a caixa com 2 pares de cada número).
public record EntradaGradeItem(int ProdutoId, [Range(0.001, 999999)] decimal Quantidade);
public record EntradaGradeRequest(
    [MinLength(1, ErrorMessage = "Informe a quantidade de pelo menos uma variação.")] List<EntradaGradeItem> Itens,
    [Range(0, 999999)] decimal? CustoUnitario,
    [StringLength(200)] string? Observacao);
