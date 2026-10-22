using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// ----- Caixa -----
public record AbrirCaixaRequest(
    [Range(1, 999, ErrorMessage = "Número do caixa inválido.")] int NumeroCaixa, // o limite real é o da empresa
    [Range(0, 10000, ErrorMessage = "Troco inicial inválido.")] decimal ValorAbertura);

public record MovimentoCaixaRequest(
    [Range(0.01, 100000, ErrorMessage = "Informe um valor maior que zero.")] decimal Valor,
    [Required(ErrorMessage = "Informe o motivo.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Motivo de 3 a 150 caracteres.")] string Motivo);

public record FecharCaixaRequest(
    [Range(0, 1000000, ErrorMessage = "Valor contado inválido.")] decimal ValorContado,
    [StringLength(300)] string? Observacao);

// Os caixas da empresa (1 até o limite do plano) e quem está em cada um agora.
public record CaixaDisponivel(int Numero, string? OcupadoPor);

public record TotalPorForma(string Forma, decimal Valor);

public record ResumoCaixaResponse(
    int SessaoId,
    int NumeroCaixa,
    string Operador,
    string Status,
    DateTimeOffset AbertaEm,
    DateTimeOffset? FechadaEm,
    decimal ValorAbertura,
    int QuantidadeVendas,
    decimal TotalVendido,
    decimal TicketMedio,
    IReadOnlyList<TotalPorForma> PorForma,
    decimal TrocoDado,
    decimal Sangrias,
    decimal Suprimentos,
    decimal DinheiroEsperado,
    int VendasCanceladas,
    decimal ValorCancelado,
    decimal? ValorContado,
    decimal? Diferenca,
    string? ObservacaoFechamento,
    IReadOnlyList<TotalPorForma>? RecebimentosFiado = null, // clientes que vieram pagar o fiado neste caixa
    decimal Devolucoes = 0); // dinheiro devolvido em estornos de vendas de caixas já fechados

// ----- Venda -----
public record ItemVendaRequest(
    [Range(1, int.MaxValue)] int ProdutoId,
    [Range(0.001, 100000, ErrorMessage = "Quantidade inválida.")] decimal Quantidade);

public record PagamentoRequest(
    [Required] string Forma,
    [Range(0.01, 1000000, ErrorMessage = "Valor de pagamento inválido.")] decimal Valor,
    int? CobrancaPixId = null,    // Pix automático: a cobrança paga no Mercado Pago
    int? ValeTrocaId = null,      // vale-troca (achado pelo código antes de pagar)
    int Parcelas = 1);            // crédito: em quantas vezes (1 a 12)

public record NovaVendaRequest(
    [MinLength(1, ErrorMessage = "A venda precisa de pelo menos um item.")] List<ItemVendaRequest> Itens,
    [Range(0, 1000000, ErrorMessage = "Desconto inválido.")] decimal Desconto,
    [MinLength(1, ErrorMessage = "Informe a forma de pagamento.")] List<PagamentoRequest> Pagamentos,
    int? ClienteId = null, // cliente identificado (obrigatório quando há pagamento no fiado)
    Guid? IdLocal = null,  // criado no caixa: se a internet cair na resposta e o caixa guardar a venda offline, ela não entra duas vezes
    int? VendedorId = null); // quem atendeu (comissão)

// ----- Caixa OFFLINE (vendas feitas sem internet, enviadas quando ela volta) -----
// Catálogo que o caixa guarda no computador para vender sem internet (só o necessário).
// ModeloId/Tamanho/Cor: grade (o caixa offline também escolhe o tamanho). Servico: vende sem estoque.
public record ProdutoCatalogoPdv(int Id, string Nome, string? CodigoBarras, string Unidade, decimal PrecoVenda, decimal EstoqueAtual,
    int? ModeloId = null, string? Tamanho = null, string? Cor = null, bool Servico = false);

public record ItemVendaOfflineRequest(
    [Range(1, int.MaxValue)] int ProdutoId,
    [Range(0.001, 100000, ErrorMessage = "Quantidade inválida.")] decimal Quantidade,
    [Range(0.01, 1000000, ErrorMessage = "Preço inválido.")] decimal PrecoUnitario); // o preço que o cliente pagou

public record VendaOfflineRequest(
    Guid IdLocal,                 // criado no caixa: a mesma venda nunca entra duas vezes
    DateTimeOffset DataHora,      // quando foi feita (no relógio do caixa)
    [MinLength(1, ErrorMessage = "A venda precisa de pelo menos um item.")] List<ItemVendaOfflineRequest> Itens,
    [Range(0, 1000000, ErrorMessage = "Desconto inválido.")] decimal Desconto,
    [MinLength(1, ErrorMessage = "Informe a forma de pagamento.")] List<PagamentoRequest> Pagamentos,
    int? VendedorId = null,
    int? ClienteId = null);

// Item/compra cancelado sem internet: o gerente foi escolhido na lista, mas a senha não pôde ser conferida.
public record CancelamentoOfflineRequest(
    Guid IdLocal,
    DateTimeOffset DataHora,
    [Required] string Tipo,
    [MinLength(1)] List<ItemVendaRequest> Itens,
    [Required, StringLength(200, MinimumLength = 3, ErrorMessage = "Motivo de 3 a 200 caracteres.")] string Motivo,
    string? GerenteId);

// ----- Autorização do gerente (cancelar item, compra e venda) -----
// O gerente escolhe o nome dele e digita a senha no caixa. Se quem está logado é gerente, vai sem autorização.
public record AutorizacaoRequest(string GerenteId, [StringLength(200)] string Senha);

public record GerenteResponse(string Id, string Nome);

// Cancelar no PDV, ANTES de pagar: um item (ou parte dele) ou a compra inteira. Os itens vão para o valor do registro.
public record CancelamentoPdvRequest(
    [Required] string Tipo, // "Item" ou "Compra"
    [MinLength(1, ErrorMessage = "Nenhum item para cancelar.")] List<ItemVendaRequest> Itens,
    [Required(ErrorMessage = "Informe o motivo.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Motivo de 3 a 200 caracteres.")] string Motivo,
    AutorizacaoRequest? Autorizacao = null);

public record CancelamentoPdvResponse(string AutorizadoPor, decimal Valor);

// Pix automático no estorno: Devolver (o Mercado Pago devolve ao cliente), Manter (fica como "Pix recebido sem venda",
// para usar numa nova venda; só com o caixa aberto) ou PorFora (a loja devolve por conta própria).
public record CancelarVendaRequest(
    [Required(ErrorMessage = "Informe o motivo.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Motivo de 3 a 200 caracteres.")] string Motivo,
    AutorizacaoRequest? Autorizacao = null,
    string? Pix = null);

// O que acontece com o dinheiro de cada forma se a venda for cancelada (a tela mostra antes de confirmar).
public record EstornoForma(string Forma, decimal Valor, string Como, bool PixAutomatico = false);

public record EstornoPreviaResponse(
    bool Pode,
    string? Bloqueio,              // por que não dá (já cancelada, gaveta sem dinheiro…)
    bool CaixaAberto,              // o caixa da venda ainda está aberto
    bool PrecisaAutorizacao,       // quem está logado não é gerente: precisa da senha de um
    IReadOnlyList<EstornoForma> Formas,
    string? Aviso);                // ex.: caixa já fechado → o estorno entra no Financeiro

// Linha da tela Vendas → Cancelamentos.
public record AutorizacaoResponse(
    int Id, DateTimeOffset DataHora, string Acao, int? NumeroCaixa, string Operador, string AutorizadoPor,
    string Motivo, string Descricao, decimal Valor, int? VendaId, bool SemConferencia = false);

public record ItemVendaResponse(int ProdutoId, string Descricao, string Unidade, decimal Quantidade, decimal PrecoUnitario, decimal Total);

// Vale-troca: o código, o saldo que ficou e até quando vale (impresso no cupom).
public record PagamentoResponse(string Forma, decimal Valor, bool PixConfirmado = false,
    string? ValeCodigo = null, decimal? ValeSaldo = null, DateOnly? ValeValidade = null, int Parcelas = 1);

public record VendaResponse(
    int Id,
    DateTimeOffset DataHora,
    int NumeroCaixa,
    string Operador,
    string Status,
    decimal Subtotal,
    decimal Desconto,
    decimal Total,
    decimal ValorPago,
    decimal Troco,
    IReadOnlyList<ItemVendaResponse> Itens,
    IReadOnlyList<PagamentoResponse> Pagamentos,
    string? MotivoCancelamento,
    ClienteCupom? Cliente = null,
    CancelamentoCupom? Cancelamento = null,
    bool Offline = false,
    Guid? IdLocal = null,
    string? Vendedor = null);

// Venda cancelada: quando, quem cancelou e quem autorizou.
public record CancelamentoCupom(DateTimeOffset? Em, string? Por, string? AutorizadoPor);

// Cliente identificado na venda; no fiado, quanto ele deve AGORA e quando esta compra vence (sai no cupom para assinar).
public record ClienteCupom(string Nome, string? Apelido, decimal? SaldoDevedor, DateOnly? VencimentoFiado);

public record VendaListaResponse(int Id, DateTimeOffset DataHora, int NumeroCaixa, string Operador, string Status, int QuantidadeItens, decimal Total, string Formas,
    bool PixConfirmado = false, string? Origem = null, bool Offline = false, bool PrecoDivergente = false);

// Tela Vendas: a página da lista + os totais do filtro inteiro (não só da página).
public record ListaVendasResponse(
    IReadOnlyList<VendaListaResponse> Itens, int Total, int PaginaAtual, int TamanhoPagina,
    int Concluidas, decimal TotalVendido, decimal TicketMedio, int Canceladas, decimal ValorCancelado,
    IReadOnlyList<int> Caixas);

public record VendasPorHora(int Hora, int Quantidade, decimal Total);

public record ResumoVendasResponse(
    DateOnly Data,
    int QuantidadeVendas,
    decimal TotalVendido,
    decimal TicketMedio,
    decimal LucroBruto,
    IReadOnlyList<TotalPorForma> PorForma,
    IReadOnlyList<VendasPorHora> PorHora);
