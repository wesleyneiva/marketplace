using System.Text.Json.Serialization;

namespace Marketplace.Api.Models;

// ===================== FINANCEIRO =====================
// Todo dinheiro que entra ou sai vira um MovimentoFinanceiro, sempre com duas respostas:
//   DE ONDE (ContaFinanceira: gaveta, cofre, banco) e POR QUÊ (CategoriaFinanceira: aluguel, energia...).
// O saldo de uma conta NUNCA fica gravado: é sempre saldo inicial + entradas − saídas (não tem como desencontrar).

// Plano de contas gerencial: as categorias ficam dentro de GRUPOS fixos (a DRE sai dos grupos);
// as categorias o cliente cria, renomeia e desativa à vontade.
public class CategoriaFinanceira : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Codigo { get; set; } = "";       // "6.02" = grupo 6, segunda categoria (gerado sozinho)
    public string Nome { get; set; } = "";
    public GrupoFinanceiro Grupo { get; set; }
    public NaturezaFinanceira Natureza { get; set; }

    // Categorias que o PRÓPRIO SISTEMA usa (ex.: "juros" ao pagar boleto atrasado). Não podem ser desativadas.
    // A chave é fixa (o nome o cliente pode trocar): "vendas-pdv", "fornecedores", "juros"...
    public string? Chave { get; set; }
    public bool Ativa { get; set; } = true;
}

// A ordem (e o número) dos grupos é a ordem da DRE e do código das categorias.
[JsonConverter(typeof(JsonStringEnumConverter<GrupoFinanceiro>))]
public enum GrupoFinanceiro
{
    ReceitaVendas = 1,      // vendas do PDV
    OutrasReceitas = 2,     // rendimentos, descontos obtidos...
    ImpostosVendas = 3,     // Simples Nacional (DAS)
    ComprasMercadorias = 4, // pagar fornecedor NÃO é despesa: vira estoque (na DRE entra o CMV)
    Pessoal = 5,
    Ocupacao = 6,
    Administrativas = 7,
    DespesasVendas = 8,     // taxas de cartão, sacolas, marketing
    Financeiras = 9,        // juros, tarifas bancárias
    ForaDoResultado = 10,   // investimentos, empréstimos, sócios: mexem no caixa, não no lucro
}

[JsonConverter(typeof(JsonStringEnumConverter<NaturezaFinanceira>))]
public enum NaturezaFinanceira { Entrada, Saida }

// Onde o dinheiro está.
public class ContaFinanceira : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public string Nome { get; set; } = "";
    public TipoContaFinanceira Tipo { get; set; }

    // Dados do banco (opcionais; ajudam a conferir o extrato).
    public string? Banco { get; set; }
    public string? Agencia { get; set; }
    public string? Numero { get; set; }

    // Implantação: "em 01/10/2026 eu tinha R$ 8.500 no banco". Movimentos ANTES desta data não contam no saldo
    // (o cliente que já tem anos de mercado começa daqui, e o histórico importado fica só para consulta).
    public decimal SaldoInicial { get; set; }
    public DateOnly SaldoInicialEm { get; set; }

    // Contas que o sistema usa sozinho ("dinheiro" = gaveta da loja). O cliente pode renomear, não desativar.
    public string? Chave { get; set; }
    public bool Ativa { get; set; } = true;
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter<TipoContaFinanceira>))]
public enum TipoContaFinanceira
{
    Dinheiro,    // gaveta dos caixas, cofre
    Banco,       // conta corrente, poupança, conta digital
    Maquininha,  // recebíveis de cartão ainda não depositados (Etapa 2)
}

// O livro: uma linha por entrada ou saída de dinheiro de uma conta.
public class MovimentoFinanceiro : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public DateOnly Data { get; set; }
    public TipoMovimentoFinanceiro Tipo { get; set; }
    public decimal Valor { get; set; }              // sempre POSITIVO; o Tipo diz se entrou ou saiu

    public int ContaFinanceiraId { get; set; }
    public ContaFinanceira? ContaFinanceira { get; set; }

    // Vazia só em transferência entre contas (dinheiro que só mudou de lugar não é receita nem despesa).
    public int? CategoriaFinanceiraId { get; set; }
    public CategoriaFinanceira? CategoriaFinanceira { get; set; }

    public string Descricao { get; set; } = "";
    public string? Documento { get; set; }
    public OrigemMovimentoFinanceiro Origem { get; set; }

    // De onde veio (para desfazer certinho).
    public int? ContaPagarId { get; set; }
    public ContaPagar? ContaPagar { get; set; }
    public Guid? TransferenciaId { get; set; }      // as duas pernas da transferência têm o mesmo Guid
    public int? SessaoCaixaId { get; set; }         // lançado no fechamento deste caixa (vendas, sangria, quebra)
    public SessaoCaixa? SessaoCaixa { get; set; }
    public Guid? LiquidacaoId { get; set; }         // recebimento da maquininha: os recebíveis desse depósito têm o mesmo Guid

    // Conferido com o extrato do banco (Etapa 5: conciliação por OFX).
    public bool Conciliado { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public string? CriadoPorId { get; set; }
    public Usuario? CriadoPor { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TipoMovimentoFinanceiro>))]
public enum TipoMovimentoFinanceiro { Entrada, Saida }

[JsonConverter(typeof(JsonStringEnumConverter<OrigemMovimentoFinanceiro>))]
public enum OrigemMovimentoFinanceiro
{
    Manual,        // receita/despesa avulsa lançada na tela
    ContaPagar,    // pagamento de uma conta a pagar (e os juros/desconto dela)
    Transferencia, // ex.: depositar o dinheiro do cofre no banco
    Importacao,    // implantação de cliente que já tem histórico (planilha / OFX)
    Caixa,         // fechamento do caixa do PDV: vendas em dinheiro/PIX, sangria, suprimento, quebra/sobra
    Recebimento,   // a maquininha depositou os recebíveis de cartão (bruto + taxa)
    Fiado,         // cliente pagou o fiado fora do caixa (ex.: PIX direto no banco)
}

// ===================== RECEBÍVEIS (o que a maquininha ainda vai depositar) =====================

// Taxa, prazo e conta de destino de cada forma de pagamento que NÃO é dinheiro (o contrato da maquininha).
public class ConfiguracaoRecebimento : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public FormaPagamento Forma { get; set; }        // Pix, Debito ou Credito
    public decimal TaxaPercentual { get; set; }      // 1,37 = 1,37% do valor vendido
    public int PrazoDias { get; set; }               // 0 = na hora (PIX); 1 = D+1; 30 = D+30
    public bool DiasUteis { get; set; }              // conta só dias úteis (D+1 útil do débito)

    public int ContaFinanceiraId { get; set; }       // onde o dinheiro cai (banco)
    public ContaFinanceira? ContaFinanceira { get; set; }
}

// Um recebível: as vendas de UM caixa, numa forma (débito ou crédito), que a maquininha vai depositar no dia previsto.
// Valores e taxa ficam COPIADOS do dia da venda: mudar o contrato amanhã não muda o que já foi vendido.
public class Recebivel : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public int SessaoCaixaId { get; set; }
    public SessaoCaixa? SessaoCaixa { get; set; }
    public FormaPagamento Forma { get; set; }

    public DateOnly DataVenda { get; set; }
    public DateOnly Previsao { get; set; }           // quando a maquininha deposita

    public decimal ValorBruto { get; set; }          // o que o cliente pagou
    public decimal TaxaPercentual { get; set; }
    public decimal Taxa { get; set; }                // em R$ (o que a maquininha fica)
    public decimal ValorLiquido { get; set; }        // bruto − taxa (o que cai na conta)

    public int ContaFinanceiraId { get; set; }       // onde vai cair
    public ContaFinanceira? ContaFinanceira { get; set; }

    public StatusRecebivel Status { get; set; } = StatusRecebivel.Previsto;
    public DateOnly? RecebidoEm { get; set; }
    public Guid? LiquidacaoId { get; set; }          // os movimentos do depósito (entrada + taxa) têm o mesmo Guid
}

[JsonConverter(typeof(JsonStringEnumConverter<StatusRecebivel>))]
public enum StatusRecebivel { Previsto, Recebido }

// Chaves fixas das categorias e contas que o sistema usa sozinho.
public static class ChavesFinanceiras
{
    public const string VendasPdv = "vendas-pdv";
    public const string OutrasReceitas = "outras-receitas";
    public const string DescontosObtidos = "descontos-obtidos";
    public const string Fornecedores = "fornecedores";
    public const string OutrasDespesas = "outras-despesas";
    public const string TaxasCartao = "taxas-cartao";
    public const string Juros = "juros";
    public const string QuebraCaixa = "quebra-caixa";
    public const string SobraCaixa = "sobra-caixa";
    public const string RecebimentoFiado = "recebimento-fiado";
    public const string Inadimplencia = "inadimplencia";

    public const string ContaDinheiro = "dinheiro";
    public const string ContaCofre = "cofre";
    public const string ContaBanco = "banco";
}
