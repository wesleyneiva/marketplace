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

    // Fluxo de caixa: o "colchão" que o dono quer manter. Saldo projetado abaixo disso = alerta amarelo; abaixo de 0 = vermelho.
    public decimal SaldoMinimoCaixa { get; set; }

    // Pix da loja: o caixa mostra um QR Code com o valor da venda e o dinheiro cai direto nesta chave (sem intermediário).
    // Tipo: Cpf, Cnpj, Celular, Email ou Aleatoria. A chave fica no jeito que o Banco Central pede (ver Services/Pix/PixQrCode.cs).
    public string? PixTipoChave { get; set; }
    public string? PixChave { get; set; }
    public string? PixNomeRecebedor { get; set; }   // como aparece para o cliente no app do banco (até 25 letras)

    // Pix AUTOMÁTICO: o provedor avisa o Marcus que o Pix caiu (ver Models/PixAutomatico.cs). Vazio = Pix manual (QR da chave acima).
    public string? PixAutomatico { get; set; }               // "MercadoPago" ou "BancoDoBrasil" (ProvedoresPix)
    public string? MercadoPagoToken { get; set; }            // Access Token CRIPTOGRAFADO (DataProtection); nunca volta para a tela
    public string? MercadoPagoTokenFinal { get; set; }       // só os 4 últimos caracteres, para a tela mostrar "••••1234"
    public string? MercadoPagoConta { get; set; }            // apelido/e-mail da conta, conferido ao salvar
    public bool MercadoPagoTeste { get; set; }               // credenciais de teste: o Pix de teste é aprovado sozinho
    public string? PixAvisoToken { get; set; }               // segredo do endereço do aviso (webhook) desta loja

    // Pix automático pelo BANCO da loja (API Pix do Banco Central; hoje o Banco do Brasil). O dinheiro cai direto na conta
    // da loja, na chave Pix cadastrada acima (que precisa ser dessa conta). Ver Services/Pix/PixBancoCentral.cs.
    public string? PixBancoClientId { get; set; }            // client_id da aplicação no portal do banco
    public string? PixBancoSegredos { get; set; }            // client_secret + chave do desenvolvedor, CRIPTOGRAFADOS (DataProtection)
    public string? PixBancoCertificado { get; set; }         // certificado digital .pfx + senha, CRIPTOGRAFADOS (produção do BB exige)
    public string? PixBancoCertificadoInfo { get; set; }     // para a tela: "EMPRESA LTDA:12345678000190 · vence 10/2027"
    public bool PixBancoTeste { get; set; }                  // ambiente de testes (homologação) do banco: sem dinheiro de verdade

    // DRE: alíquota EFETIVA do Simples Nacional (% do faturamento), informada pelo contador. 0 = ainda não informada
    // (a DRE usa os DAS lançados no mês e avisa). Ex.: 7,3 = 7,3% das vendas do mês.
    public decimal AliquotaSimples { get; set; }

    // Dias e horas em que a loja abre, regra dos feriados e feriados da cidade (ver HorarioFuncionamento).
    public HorarioFuncionamento Horario { get; set; } = HorarioFuncionamento.Padrao();

    // Vendas automáticas (o "bot"): clientes simulados comprando no horário da loja. Só o dono da plataforma liga.
    // Desligado (clientes de verdade) · SoVendas (testar o sistema: o bot vende, as pessoas cuidam de compras e contas)
    // · Completo (a demonstração: o bot também compra, recebe entregas, paga contas e usa o fiado).
    public string VendasAutomaticas { get; set; } = ModosVendasAutomaticas.Desligado;

    // Trocas: até quantos dias depois da compra o cliente troca sem precisar do gerente, e quanto tempo vale o vale-troca.
    public int PrazoTrocaDias { get; set; } = 30;
    public int ValidadeValeDias { get; set; } = 90;

    // Ramo do comércio (ver Ramos): decide as categorias iniciais, as palavras das telas ("mercado" / "loja"), o que
    // aparece (balança, validade) e o que a IA leva em conta. Só o dono da plataforma muda.
    public string Ramo { get; set; } = Ramos.Mercado;

    // Módulos liberados para este cliente (ver Models/Modulos.cs). null = todos. Só o dono da plataforma muda.
    public string[]? Modulos { get; set; }

    // A "empresa" do DONO DA PLATAFORMA (WN Labs): não é um mercado. Quem é Administrador dela entra na área do dono
    // (clientes, planos, cobranças) e não vê telas de mercado. Só existe uma (criada pelo comando criar-dono).
    public bool Plataforma { get; set; }

    // Ficha do cliente (preenchida pelo dono da plataforma; o cliente não vê nem muda).
    public string? RazaoSocial { get; set; }
    public string? Documento { get; set; }   // CPF ou CNPJ, só números
    public string? Responsavel { get; set; }
    public string? Telefone { get; set; }    // WhatsApp, só números com DDD
    public string? EmailContato { get; set; }
    public string? Endereco { get; set; }
    public string? Observacoes { get; set; }

    // Assinatura (área do dono). Sem plano = ainda não cobra.
    public int? PlanoId { get; set; }
    public Plano? Plano { get; set; }
    public int DiaVencimento { get; set; } = 10;     // 1 a 28
    public DateOnly? TesteAte { get; set; }          // teste grátis: não cobra mensalidades que vencem até esse dia
    public DateOnly? CobrarDesde { get; set; }       // primeira mensalidade (vencimento) a partir desse dia
    public decimal? PrecoCombinado { get; set; }     // valor especial (desconto); vazio = pelo plano
    public bool Cortesia { get; set; }               // não cobra (demonstração, testes, parceiro)
    public string? MotivoSuspensao { get; set; }     // Atraso (volta sozinho ao pagar) ou Manual
    public DateTimeOffset? SuspensaEm { get; set; }
}

// Ramos de comércio atendidos. O sistema é um só: o ramo só muda textos, sugestões e o que fica à vista.
// O Angular tem a mesma lista em core/ramos.ts.
public static class Ramos
{
    public const string Mercado = "mercado";
    public const string Esporte = "esporte"; // loja de material esportivo (raquetes, calçados, roupas, cordas…)

    public static readonly string[] Todos = [Mercado, Esporte];
    public static bool Valido(string? ramo) => ramo is not null && Todos.Contains(ramo);

    // Categorias criadas junto com a empresa (vazias; o cliente muda à vontade).
    public static string[] CategoriasPadrao(string ramo) => ramo switch
    {
        Esporte => ["Raquetes", "Calçados", "Roupas", "Bolas", "Cordas", "Acessórios", "Bolsas e mochilas", "Serviços"],
        _ => ["Mercearia", "Bebidas", "Hortifrúti", "Padaria", "Açougue", "Frios e Laticínios", "Limpeza", "Higiene e Beleza"],
    };

    // Como a IA e os textos do servidor chamam o negócio.
    public static string Negocio(string ramo) => ramo == Esporte ? "loja de material esportivo" : "mercado";
}

public static class ModosVendasAutomaticas
{
    public const string Desligado = "Desligado";
    public const string SoVendas = "SoVendas";
    public const string Completo = "Completo";

    public static bool Valido(string? modo) => modo is Desligado or SoVendas or Completo;
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
