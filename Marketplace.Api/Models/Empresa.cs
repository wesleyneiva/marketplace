namespace Marketplace.Api.Models;

// Cada CLIENTE do sistema (um mercado) é uma Empresa. Todos os dados (produtos, vendas, usuários...)
// pertencem a uma empresa, e ninguém enxerga os dados de outra: é o "multi-tenant" (vários inquilinos,
// um prédio só). O mercadinho de 2 caixas e o de 20 usam o MESMO sistema — muda só o cadastro daqui.
public class Empresa
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";

    // Endereço próprio no futuro: "mercadoze" → mercadoze.wnlabs.com.br (só letras minúsculas, números e hífen).
    public string Subdominio { get; set; } = "";

    // Quantos caixas (PDVs) a empresa pode usar ao mesmo tempo — o "tamanho do plano".
    public int LimiteCaixas { get; set; } = 2;

    // Empresa de demonstração (vitrine pública com o simulador vendendo).
    public bool Demonstracao { get; set; }

    // Empresa desativada (ex.: parou de pagar): ninguém dela consegue usar o sistema.
    public bool Ativa { get; set; } = true;

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;

    // Formato da etiqueta da balança (igual em todas as balanças da loja; configurado no programa da balança):
    //   2 + código do produto (4 ou 5 dígitos) + [0 se 4 dígitos] + valor (6 dígitos) + dígito verificador.
    public int BalancaDigitosCodigo { get; set; } = 4;
    // "Preco" = os 6 dígitos são o preço total em centavos (o mais comum); "Peso" = o peso em gramas.
    public string BalancaEtiqueta { get; set; } = EtiquetaBalancaTipos.Preco;

    // Onde fica a loja: define o CLIMA (simulador, relatório do clima, previsão dos insights) e o FUSO HORÁRIO
    // ("hoje", vendas do dia, relatórios por hora). O Brasil tem 4 fusos: Manaus é 1 h a menos que Brasília.
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string Fuso { get; set; } = "America/Sao_Paulo"; // nome IANA (ex.: America/Manaus)
}

public static class EtiquetaBalancaTipos
{
    public const string Preco = "Preco";
    public const string Peso = "Peso";
}

// "Etiqueta" das tabelas que pertencem a uma empresa. O AppDbContext procura todas as classes com
// esta etiqueta e (1) filtra as consultas pela empresa de quem está logado e (2) preenche o EmpresaId
// sozinho ao gravar uma linha nova.
public interface IDaEmpresa
{
    int EmpresaId { get; set; }
}
