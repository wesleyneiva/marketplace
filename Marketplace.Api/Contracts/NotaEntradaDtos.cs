namespace Marketplace.Api.Contracts;

// ---------- Conferência: o que o sistema entendeu do XML (nada é gravado) ----------

public record ConferenciaNotaResponse(
    string Chave,
    string Numero,
    string Serie,
    DateTimeOffset DataEmissao,
    decimal ValorTotal,
    FornecedorNotaResponse Fornecedor,
    string? Bloqueio,                 // preenchido = esta nota NÃO pode entrar (ex.: já foi lançada)
    List<string> Avisos,
    List<PedidoAbertoResponse> PedidosAbertos,
    int? PedidoSugeridoId,
    List<ItemConferenciaResponse> Itens,
    PagamentoNotaResponse Pagamento);

// Como a nota vai ser paga: as parcelas (duplicatas do boleto) e a forma de pagamento informada pelo fornecedor.
public record PagamentoNotaResponse(List<ParcelaNotaResponse> Parcelas, List<string> Formas);

public record ParcelaNotaResponse(string? Numero, DateOnly Vencimento, decimal Valor);

public record FornecedorNotaResponse(int? Id, string Nome, string RazaoSocial, string Cnpj, bool Novo);

public record PedidoAbertoResponse(int Id, DateTimeOffset CriadoEm, DateOnly? PrevisaoEntrega, int QuantidadeItens, decimal ValorEstimado);

public record ItemConferenciaResponse(
    int NumeroItem,
    string CodigoFornecedor,
    string? CodigoBarras,          // o da unidade (cEANTrib) ou, se não houver, o da embalagem (cEAN)
    string Descricao,
    string? Ncm,
    string UnidadeNota,            // como o fornecedor vendeu (CX, FD, UN, KG…)
    decimal QuantidadeNota,
    decimal CustoTotal,            // produtos − desconto + frete/IPI/ST: o que o item custou de verdade
    ProdutoConferenciaResponse? Produto, // null = não achou: cadastrar novo ou escolher um existente
    string? ComoAchou,             // "vínculo" (já usado antes) ou "código de barras"
    decimal Fator,                 // 1 embalagem da nota = Fator unidades do produto
    bool FatorVeioDaNota,          // o fator foi deduzido do XML (qTrib/qCom) ou do vínculo salvo
    DateOnly? Validade,            // quando a nota traz (<rastro>)
    NovoProdutoSugestao Sugestao,  // pré-preenchimento para cadastrar como produto novo
    List<string> Avisos);

public record ProdutoConferenciaResponse(
    int Id, string Nome, string? CodigoBarras, string Unidade, bool ControlaValidade, bool Ativo,
    decimal PrecoCusto, decimal PrecoVenda);

public record NovoProdutoSugestao(
    string Nome, string? CodigoBarras, int? CategoriaId, string Unidade, bool ControlaValidade, decimal? PrecoVenda);

// ---------- Gravação: as decisões da pessoa, item a item ----------

// GerarContas: lança as parcelas em Contas a pagar. JaPaga (nota sem parcelas, paga na entrega): a conta já entra paga.
public record RegistrarNotaRequest(int? PedidoCompraId, List<DecisaoItemNota> Itens, bool GerarContas = true, bool JaPaga = false);

// Acao: "existente" (ProdutoId), "novo" (Novo) ou "ignorar" (ex.: brinde, item que não é revenda).
public record DecisaoItemNota(int NumeroItem, string Acao, int? ProdutoId, NovoProdutoNota? Novo, decimal Fator, DateOnly? Validade);

public record NovoProdutoNota(string Nome, string? CodigoBarras, int CategoriaId, string Unidade, decimal PrecoVenda, bool ControlaValidade);

public record RegistroNotaResponse(
    int NotaId, int ItensLancados, int ItensIgnorados, int ProdutosCriados, bool FornecedorCriado, string Fornecedor,
    int? PedidoRecebidoId,
    int ContasCriadas);

// ---------- Histórico ----------

public record NotaEntradaResponse(
    int Id, string Chave, string Numero, string Serie, DateTimeOffset DataEmissao, decimal ValorTotal, int QuantidadeItens,
    string Fornecedor, int? PedidoCompraId, DateTimeOffset RegistradaEm, string RegistradaPor);
