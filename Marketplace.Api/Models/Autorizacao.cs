using System.Text.Json.Serialization;

namespace Marketplace.Api.Models;

// "Chama o gerente!" — tudo o que o operador de caixa só faz com a senha de um gerente (ou que o próprio gerente fez)
// fica registrado aqui: tirar um item da compra, cancelar a compra inteira e cancelar uma venda já paga (com estorno).
// É o que o dono confere em Vendas → Cancelamentos ("quem tirou o quê, quando, por quê e quem liberou").
public class AutorizacaoGerente : IDaEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }

    public DateTimeOffset DataHora { get; set; } = DateTimeOffset.UtcNow;
    public AcaoAutorizada Acao { get; set; }

    public int? SessaoCaixaId { get; set; }
    public SessaoCaixa? SessaoCaixa { get; set; }

    public string OperadorId { get; set; } = "";       // quem estava logado (o operador do caixa)
    public Usuario? Operador { get; set; }
    public string AutorizadoPorId { get; set; } = "";  // o gerente que liberou (o próprio operador, se ele é gerente)
    public Usuario? AutorizadoPor { get; set; }

    public string Motivo { get; set; } = "";
    public string Descricao { get; set; } = "";        // "2 × Arroz 5 kg" · "Compra com 7 itens" · "Venda #123"
    public decimal Valor { get; set; }                 // calculado com o preço do banco (nunca o da tela)

    public int? VendaId { get; set; }
    public Venda? Venda { get; set; }

    // Feito SEM INTERNET: o gerente foi escolhido na lista, mas a senha não pôde ser conferida (aparece em destaque).
    public bool SemConferencia { get; set; }
    public Guid? IdLocal { get; set; } // o mesmo registro não entra duas vezes quando o caixa reenvia
}

[JsonConverter(typeof(JsonStringEnumConverter<AcaoAutorizada>))]
public enum AcaoAutorizada
{
    CancelarItem,   // item tirado da compra em andamento (antes de pagar)
    CancelarCompra, // compra em andamento abandonada inteira
    CancelarVenda,  // venda já paga cancelada, com estorno do dinheiro
}
