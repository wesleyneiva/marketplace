using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// ----- Clientes da loja (cadastro e histórico; o fiado continua com as telas dele) -----

public record ClienteCadastroRequest(
    [Required(ErrorMessage = "Informe o nome.")][StringLength(120, MinimumLength = 2, ErrorMessage = "Nome: de 2 a 120 letras.")] string Nome,
    [StringLength(60)] string? Apelido,
    [StringLength(20)] string? Telefone,
    [StringLength(14)] string? Cpf,
    [StringLength(120)] string? Email,
    [StringLength(200)] string? Endereco,
    DateOnly? Nascimento,
    [StringLength(120)] string? Raquete,
    [StringLength(120)] string? Corda,
    [StringLength(20)] string? Tensao,
    [StringLength(300)] string? Observacao);

// No caixa: só o necessário para identificar (o resto o gerente completa depois).
public record ClienteRapidoRequest(
    [Required(ErrorMessage = "Informe o nome.")][StringLength(120, MinimumLength = 2, ErrorMessage = "Nome: de 2 a 120 letras.")] string Nome,
    [StringLength(20)] string? Telefone,
    [StringLength(14)] string? Cpf);

public record ClienteResponse(int Id, string Nome, string? Apelido, string? Telefone, string? Cpf, string? Email, string? Endereco,
    DateOnly? Nascimento, string? Raquete, string? Corda, string? Tensao, string? Observacao, bool Ativo, bool Simulado,
    DateTimeOffset CriadoEm);

public record ClienteListaItem(int Id, string Nome, string? Apelido, string? Telefone, DateOnly? Nascimento, string? Raquete,
    int Compras, decimal TotalGasto, DateTimeOffset? UltimaCompra, bool Ativo, bool Simulado);

public record ClientesPagina(List<ClienteListaItem> Itens, int Total, int Pagina, int Tamanho, int Ativos, int AniversariantesDoMes,
    int ComprouNosUltimos30Dias);

// No caixa: a busca devolve o que ajuda o atendimento (última compra, o que usa).
public record ClienteBuscaPdv(int Id, string Nome, string? Apelido, string? TelefoneFinal, int Compras, DateTimeOffset? UltimaCompra,
    string? Raquete, string? Corda, string? Tensao, bool AniversarioHoje);

public record CompraDoCliente(int VendaId, DateTimeOffset DataHora, string Itens, int QuantidadeItens, decimal Total, string Formas,
    string? Vendedor, string Status);
public record ProdutoDoCliente(string Produto, decimal Quantidade, int Vezes, DateTimeOffset UltimaVez);
public record OrdemDoCliente(int Id, int Numero, DateTimeOffset CriadaEm, string Raquete, string? Corda, string? Tensao, string Servico,
    string Status, decimal Valor);
public record TrocaDoCliente(int Id, DateTimeOffset DataHora, int VendaId, string Itens, string Motivo, decimal Valor, string ValeCodigo,
    decimal ValeSaldo, string ValeSituacao);

public record ClienteLojaDetalheResponse(ClienteResponse Cliente, int Compras, decimal TotalGasto, decimal TicketMedio,
    DateTimeOffset? PrimeiraCompra, DateTimeOffset? UltimaCompra, int? DiasEntreCompras, int? DiasSemComprar,
    List<ProdutoDoCliente> MaisComprados, List<CompraDoCliente> UltimasCompras, List<TrocaDoCliente> Trocas,
    List<OrdemDoCliente> Ordens);

// ----- Vendedores e comissão -----

public record VendedorRequest(
    [Required(ErrorMessage = "Informe o nome.")][StringLength(80, MinimumLength = 2, ErrorMessage = "Nome: de 2 a 80 letras.")] string Nome,
    [Range(0, 30, ErrorMessage = "Comissão de 0% a 30%.")] decimal ComissaoPercentual,
    bool Ativo = true);

public record VendedorResponse(int Id, string Nome, decimal ComissaoPercentual, bool Ativo);

public record ComissaoVendedor(int VendedorId, string Nome, decimal Percentual, int Vendas, decimal Vendido, decimal Comissao,
    decimal EstornoTrocas, decimal Total, bool Lancada, int? ContaPagarId, decimal? ValorLancado);

public record ComissaoMesResponse(string Mes, DateOnly Inicio, DateOnly Fim, bool MesFechado, List<ComissaoVendedor> Vendedores,
    decimal Total, int VendasSemVendedor, decimal VendidoSemVendedor);

public record CategoriaComissao(int Id, string Nome, decimal? ComissaoPercentual);
public record CategoriaComissaoRequest(int CategoriaId, [Range(0, 30, ErrorMessage = "Comissão de 0% a 30%.")] decimal? ComissaoPercentual);
