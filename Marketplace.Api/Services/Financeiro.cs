using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Regras do livro financeiro usadas por mais de um lugar (tela de contas, entrada de NF-e, sementes).
public static class Financeiro
{
    // Id de uma categoria/conta do sistema pela chave fixa (ex.: "juros"). Toda empresa tem (SeedFinanceiro).
    public static async Task<int> CategoriaAsync(AppDbContext db, string chave) =>
        await db.CategoriasFinanceiras.Where(c => c.Chave == chave).Select(c => c.Id).FirstOrDefaultAsync() is var id and > 0
            ? id : throw new InvalidOperationException($"Categoria do sistema \"{chave}\" não existe nesta empresa.");

    public static async Task<int> ContaAsync(AppDbContext db, string chave) =>
        await db.ContasFinanceiras.Where(c => c.Chave == chave).Select(c => c.Id).FirstOrDefaultAsync() is var id and > 0
            ? id : throw new InvalidOperationException($"Conta do sistema \"{chave}\" não existe nesta empresa.");

    // Os grupos do plano de contas: nome na tela, natureza fixa (nulo = escolhe na categoria) e para que serve.
    public static readonly IReadOnlyList<(GrupoFinanceiro Grupo, string Nome, NaturezaFinanceira? Natureza, string Explicacao)> Grupos =
    [
        (GrupoFinanceiro.ReceitaVendas, "Receitas de vendas", NaturezaFinanceira.Entrada, "O que entra das vendas do PDV."),
        (GrupoFinanceiro.OutrasReceitas, "Outras receitas", NaturezaFinanceira.Entrada, "Rendimentos, descontos obtidos e outras entradas que não são venda."),
        (GrupoFinanceiro.ImpostosVendas, "Impostos sobre vendas", NaturezaFinanceira.Saida, "Simples Nacional e outros impostos calculados sobre o faturamento."),
        (GrupoFinanceiro.ComprasMercadorias, "Compras de mercadorias", NaturezaFinanceira.Saida,
            "Pagamento a fornecedores. Não é despesa: vira estoque. No resultado entra o custo do que foi VENDIDO (CMV)."),
        (GrupoFinanceiro.Pessoal, "Despesas com pessoal", NaturezaFinanceira.Saida, "Salários, encargos, benefícios e pró-labore."),
        (GrupoFinanceiro.Ocupacao, "Despesas de ocupação", NaturezaFinanceira.Saida, "Aluguel, energia, água, IPTU: o custo de manter a loja aberta."),
        (GrupoFinanceiro.Administrativas, "Despesas administrativas", NaturezaFinanceira.Saida, "Contador, internet, sistemas, manutenção e outras."),
        (GrupoFinanceiro.DespesasVendas, "Despesas com vendas", NaturezaFinanceira.Saida, "Taxas de cartão, sacolas, marketing."),
        (GrupoFinanceiro.Financeiras, "Despesas financeiras", NaturezaFinanceira.Saida, "Juros, multas e tarifas bancárias."),
        (GrupoFinanceiro.ForaDoResultado, "Fora do resultado", null,
            "Investimentos, empréstimos e movimentos dos sócios: mexem no caixa, mas não são lucro nem prejuízo."),
    ];

    public static string NomeGrupo(GrupoFinanceiro g) => Grupos.First(x => x.Grupo == g).Nome;
    public static NaturezaFinanceira? NaturezaDoGrupo(GrupoFinanceiro g) => Grupos.First(x => x.Grupo == g).Natureza;

    // Saldo de cada conta no FIM do dia "ate" (nulo = tudo). Só contam movimentos a partir do saldo inicial.
    public static async Task<Dictionary<int, decimal>> SaldosAsync(AppDbContext db, DateOnly? ate = null)
    {
        var contas = await db.ContasFinanceiras.AsNoTracking()
            .Select(c => new
            {
                c.Id, c.SaldoInicialEm,
                Inicial = ate == null || c.SaldoInicialEm <= ate ? c.SaldoInicial : 0m,
                Movido = db.MovimentosFinanceiros
                    .Where(m => m.ContaFinanceiraId == c.Id && m.Data >= c.SaldoInicialEm && (ate == null || m.Data <= ate))
                    .Sum(m => m.Tipo == TipoMovimentoFinanceiro.Entrada ? m.Valor : -m.Valor),
            }).ToListAsync();
        return contas.ToDictionary(c => c.Id, c => c.Inicial + c.Movido);
    }

    // Dinheiro físico (gaveta, cofre) não fica negativo: se ficar, alguém lançou errado ou falta dinheiro.
    // (Banco pode: cheque especial.) Devolve a mensagem de erro, ou nulo se pode tirar "valor" da conta.
    // "devolvendo" = o que a própria operação devolve à conta antes de tirar (ex.: edição do mesmo lançamento).
    public static async Task<string?> FaltaDinheiroAsync(AppDbContext db, int contaId, decimal valor, decimal devolvendo = 0)
    {
        var conta = await db.ContasFinanceiras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contaId);
        if (conta is null || conta.Tipo != TipoContaFinanceira.Dinheiro) return null;
        var saldo = (await SaldosAsync(db)).GetValueOrDefault(contaId);
        var ficaria = saldo + devolvendo - valor;
        if (ficaria >= 0) return null;
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        return $"\"{conta.Nome}\" ficaria negativo ({ficaria.ToString("C", pt)}); hoje tem {saldo.ToString("C", pt)}. "
             + "Dinheiro em espécie não fica negativo: se o dinheiro veio de outro lugar, faça antes uma transferência para esta conta.";
    }

    // Pagamento de uma conta a pagar → movimentos no livro (a conta já vem com PagaEm/ValorPago preenchidos).
    // Separa a diferença do valor original: pagou MAIS = juros e multas; pagou MENOS = desconto obtido.
    //   Boleto de R$ 100 pago por R$ 103 → saída 100 (categoria da conta) + saída 3 (Juros e multas).
    //   Boleto de R$ 100 pago por R$ 98  → saída 100 (categoria da conta) + entrada 2 (Descontos obtidos).
    // Assim a categoria mostra o custo "de verdade" e o saldo da conta bate com o que saiu (98 ou 103).
    public static async Task<List<MovimentoFinanceiro>> RegistrarPagamentoAsync(
        AppDbContext db, ContaPagar conta, int contaFinanceiraId, string? usuarioId)
    {
        if (conta.PagaEm is not DateOnly data || conta.ValorPago is not decimal pago)
            throw new InvalidOperationException("Conta sem data ou valor de pagamento.");
        var categoria = conta.CategoriaFinanceiraId ?? await CategoriaAsync(db, ChavesFinanceiras.OutrasDespesas);

        MovimentoFinanceiro Movimento(TipoMovimentoFinanceiro tipo, decimal valor, int cat, string descricao) => new()
        {
            Data = data, Tipo = tipo, Valor = valor, ContaFinanceiraId = contaFinanceiraId, CategoriaFinanceiraId = cat,
            Descricao = descricao.Length > 200 ? descricao[..200] : descricao, Documento = conta.Documento,
            Origem = OrigemMovimentoFinanceiro.ContaPagar, ContaPagar = conta, CriadoPorId = usuarioId,
        };

        var movimentos = new List<MovimentoFinanceiro> { Movimento(TipoMovimentoFinanceiro.Saida, conta.Valor, categoria, conta.Descricao) };
        var diferenca = pago - conta.Valor;
        if (diferenca > 0)
            movimentos.Add(Movimento(TipoMovimentoFinanceiro.Saida, diferenca,
                await CategoriaAsync(db, ChavesFinanceiras.Juros), "Juros/multa · " + conta.Descricao));
        else if (diferenca < 0)
            movimentos.Add(Movimento(TipoMovimentoFinanceiro.Entrada, -diferenca,
                await CategoriaAsync(db, ChavesFinanceiras.DescontosObtidos), "Desconto obtido · " + conta.Descricao));

        conta.ContaFinanceiraId = contaFinanceiraId;
        db.MovimentosFinanceiros.AddRange(movimentos);
        return movimentos;
    }

    // Palavras que denunciam a categoria pela descrição ("Conta de luz CEEE" → Energia elétrica).
    // Chave = nome PADRÃO da categoria (se o cliente renomear, vale o nome novo dela como palavra).
    private static readonly Dictionary<string, string[]> PalavrasPorCategoria = new()
    {
        ["Aluguel"] = ["aluguel", "locacao"],
        ["Energia elétrica"] = ["energia", "luz", "ceee", "rge", "cemig", "enel", "light", "copel", "celesc", "cpfl", "equatorial", "coelba", "celpe", "neoenergia", "amazonas energia"],
        ["Água e esgoto"] = ["agua", "esgoto", "dmae", "sabesp", "corsan", "cedae", "copasa", "sanepar", "embasa", "compesa", "caesb"],
        ["IPTU e condomínio"] = ["iptu", "condominio"],
        ["Salários"] = ["salario", "salarios", "folha de pagamento", "folha", "13o", "ferias", "rescisao"],
        ["Encargos (INSS/FGTS)"] = ["inss", "fgts", "gps", "encargos", "esocial"],
        ["Benefícios (VT/VA)"] = ["vale transporte", "vale alimentacao", "vale refeicao", "vt", "va", "vr", "beneficio", "beneficios"],
        ["Pró-labore"] = ["pro-labore", "pro labore", "prolabore"],
        ["Simples Nacional (DAS)"] = ["das", "simples nacional", "simples"],
        ["Contabilidade"] = ["contador", "contabilidade", "contabil", "honorarios"],
        ["Internet e telefone"] = ["internet", "telefone", "celular", "fibra", "vivo", "claro", "tim", "oi"],
        ["Sistemas e software"] = ["sistema", "software", "licenca", "assinatura"],
        ["Material de escritório"] = ["papelaria", "escritorio", "bobina", "toner", "cartucho"],
        ["Manutenção e reparos"] = ["manutencao", "conserto", "reparo", "eletricista", "encanador", "refrigeracao"],
        ["Sacolas e embalagens"] = ["sacola", "sacolas", "embalagem", "embalagens"],
        ["Marketing e divulgação"] = ["marketing", "propaganda", "anuncio", "panfleto", "divulgacao", "instagram"],
        ["Tarifas bancárias"] = ["tarifa", "tarifas", "manutencao de conta", "pacote de servicos", "ted", "doc"],
        ["Juros e multas pagos"] = ["juros", "multa"],
        ["Taxas de cartão e PIX"] = ["maquininha", "taxa de cartao", "taxa cartao", "mdr", "antecipacao"],
        ["Pagamento de empréstimos"] = ["emprestimo", "financiamento", "parcela do banco"],
        ["Investimentos (equipamentos, reformas)"] = ["freezer", "geladeira", "balanca", "equipamento", "reforma", "computador", "impressora"],
        ["Retirada dos sócios"] = ["retirada", "distribuicao de lucro"],
        ["Rendimentos de aplicações"] = ["rendimento", "cdb", "aplicacao", "poupanca"],
    };

    // Sugere a categoria pela descrição (nulo = não reconheceu). Usada no acerto das contas antigas,
    // na tela (sugestão enquanto digita) e na implantação de cliente antigo (planilhas sem categoria).
    // Só sugere categorias ATIVAS e da natureza certa (descrição de saída não vira receita).
    public static int? SugerirCategoria(string descricao, IEnumerable<CategoriaFinanceira> categorias, NaturezaFinanceira natureza)
    {
        var texto = " " + SoPalavras(descricao) + " ";
        // Ganha a palavra MAIS LONGA encontrada (a mais específica): "manutenção de conta" é tarifa, não reparo.
        return categorias
            .Where(c => c.Ativa && c.Natureza == natureza && c.Grupo != GrupoFinanceiro.ReceitaVendas)
            .SelectMany(c => PalavrasPorCategoria.GetValueOrDefault(c.Nome, []).Append(c.Nome)
                .Select(SoPalavras).Where(p => p.Length > 0 && texto.Contains(" " + p + " "))
                .Select(p => (c.Id, p.Length)))
            .OrderByDescending(x => x.Length).Select(x => (int?)x.Id).FirstOrDefault();
    }

    // "Conta de Luz/CEEE – Out." → "conta de luz ceee out"
    private static string SoPalavras(string texto) =>
        System.Text.RegularExpressions.Regex.Replace(ImportacaoProdutosService.Normalizar(texto), "[^a-z0-9]+", " ").Trim();

    // Desfazer o pagamento: os movimentos dele saem do livro.
    public static Task<int> DesfazerPagamentoAsync(AppDbContext db, int contaPagarId) =>
        db.MovimentosFinanceiros.Where(m => m.ContaPagarId == contaPagarId).ExecuteDeleteAsync();
}
