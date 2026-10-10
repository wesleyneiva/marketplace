using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.EntityFrameworkCore;
using static Marketplace.Api.Models.ChavesFinanceiras;

namespace Marketplace.Api.Data;

// Plano de contas e contas financeiras PADRÃO de um mercadinho. Toda empresa começa com eles
// (as que já existiam ganham ao ligar a API; as novas, no cadastro). O cliente edita à vontade depois.
public static class SeedFinanceiro
{
    private const NaturezaFinanceira E = NaturezaFinanceira.Entrada, S = NaturezaFinanceira.Saida;

    private static readonly (GrupoFinanceiro Grupo, NaturezaFinanceira Natureza, string Nome, string? Chave)[] Categorias =
    [
        (GrupoFinanceiro.ReceitaVendas, E, "Vendas no PDV", VendasPdv),
        (GrupoFinanceiro.OutrasReceitas, E, "Outras receitas", OutrasReceitas),
        (GrupoFinanceiro.OutrasReceitas, E, "Rendimentos de aplicações", null),
        (GrupoFinanceiro.OutrasReceitas, E, "Descontos obtidos", DescontosObtidos),
        (GrupoFinanceiro.ImpostosVendas, S, "Simples Nacional (DAS)", null),
        (GrupoFinanceiro.ComprasMercadorias, S, "Fornecedores de mercadorias", Fornecedores),
        (GrupoFinanceiro.Pessoal, S, "Salários", null),
        (GrupoFinanceiro.Pessoal, S, "Encargos (INSS/FGTS)", null),
        (GrupoFinanceiro.Pessoal, S, "Benefícios (VT/VA)", null),
        (GrupoFinanceiro.Pessoal, S, "Pró-labore", null),
        (GrupoFinanceiro.Ocupacao, S, "Aluguel", null),
        (GrupoFinanceiro.Ocupacao, S, "Energia elétrica", null),
        (GrupoFinanceiro.Ocupacao, S, "Água e esgoto", null),
        (GrupoFinanceiro.Ocupacao, S, "IPTU e condomínio", null),
        (GrupoFinanceiro.Administrativas, S, "Contabilidade", null),
        (GrupoFinanceiro.Administrativas, S, "Internet e telefone", null),
        (GrupoFinanceiro.Administrativas, S, "Sistemas e software", null),
        (GrupoFinanceiro.Administrativas, S, "Material de escritório", null),
        (GrupoFinanceiro.Administrativas, S, "Manutenção e reparos", null),
        (GrupoFinanceiro.Administrativas, S, "Outras despesas", OutrasDespesas),
        (GrupoFinanceiro.DespesasVendas, S, "Taxas de cartão e PIX", TaxasCartao),
        (GrupoFinanceiro.DespesasVendas, S, "Sacolas e embalagens", null),
        (GrupoFinanceiro.DespesasVendas, S, "Marketing e divulgação", null),
        (GrupoFinanceiro.Financeiras, S, "Juros e multas pagos", Juros),
        (GrupoFinanceiro.Financeiras, S, "Tarifas bancárias", null),
        (GrupoFinanceiro.ForaDoResultado, S, "Investimentos (equipamentos, reformas)", null),
        (GrupoFinanceiro.ForaDoResultado, S, "Pagamento de empréstimos", null),
        (GrupoFinanceiro.ForaDoResultado, S, "Retirada dos sócios", null),
        (GrupoFinanceiro.ForaDoResultado, E, "Empréstimos recebidos", null),
        (GrupoFinanceiro.ForaDoResultado, E, "Aporte dos sócios", null),
    ];

    private static readonly (string Nome, TipoContaFinanceira Tipo, string Chave)[] Contas =
    [
        ("Caixa da loja (dinheiro)", TipoContaFinanceira.Dinheiro, ContaDinheiro),
        ("Cofre", TipoContaFinanceira.Dinheiro, ContaCofre),
        ("Conta bancária", TipoContaFinanceira.Banco, ContaBanco),
    ];

    // Ao ligar a API: garante o padrão em TODAS as empresas e acerta as contas a pagar de antes do financeiro.
    public static async Task ExecutarAsync(IServiceProvider services)
    {
        List<(int Id, string Fuso)> empresas;
        using (var scope = services.CreateScope())
        {
            empresas = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Empresas.AsNoTracking()
                .Select(e => new { e.Id, e.Fuso }).ToListAsync()).Select(e => (e.Id, e.Fuso)).ToList();
        }
        foreach (var (id, fuso) in empresas)
        {
            using var scope = services.CriarEscopo(id);
            Relogio.UsarFuso(fuso);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await GarantirPadraoAsync(db);
            await AcertarContasAntigasAsync(db);
        }
        Relogio.UsarFuso(null);
    }

    // Cria o que faltar (pela chave ou pelo nome): pode rodar quantas vezes quiser.
    public static async Task GarantirPadraoAsync(AppDbContext db)
    {
        var existentes = await db.CategoriasFinanceiras.ToListAsync();
        foreach (var (grupo, natureza, nome, chave) in Categorias)
        {
            if (existentes.Any(c => (chave is not null && c.Chave == chave) || c.Nome == nome)) continue;
            var categoria = new CategoriaFinanceira
            {
                Grupo = grupo, Natureza = natureza, Nome = nome, Chave = chave, Codigo = ProximoCodigo(existentes, grupo),
            };
            existentes.Add(categoria);
            db.CategoriasFinanceiras.Add(categoria);
        }

        var contas = await db.ContasFinanceiras.ToListAsync();
        // Saldo inicial "a partir de": o primeiro pagamento já registrado (empresa antiga) ou hoje (empresa nova).
        var desde = await db.ContasPagar.Where(c => c.PagaEm != null).MinAsync(c => c.PagaEm) is DateOnly d && d < Relogio.Hoje ? d : Relogio.Hoje;
        foreach (var (nome, tipo, chave) in Contas)
        {
            if (contas.Any(c => c.Chave == chave || c.Nome == nome)) continue;
            db.ContasFinanceiras.Add(new ContaFinanceira { Nome = nome, Tipo = tipo, Chave = chave, SaldoInicialEm = desde });
        }
        await db.SaveChangesAsync();
    }

    // Próximo código livre no grupo: grupo 6 com 6.01 e 6.02 → "6.03".
    public static string ProximoCodigo(IEnumerable<CategoriaFinanceira> existentes, GrupoFinanceiro grupo)
    {
        var prefixo = $"{(int)grupo}.";
        var maior = existentes.Where(c => c.Grupo == grupo && c.Codigo.StartsWith(prefixo))
            .Select(c => int.TryParse(c.Codigo[prefixo.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"{prefixo}{maior + 1:00}";
    }

    // Contas a pagar de ANTES do financeiro: ganham categoria e, se já pagas, o movimento na conta bancária
    // (sem ele o saldo do banco não bateria com o que já saiu).
    private static async Task AcertarContasAntigasAsync(AppDbContext db)
    {
        var fornecedores = await Financeiro.CategoriaAsync(db, Fornecedores);
        var outras = await Financeiro.CategoriaAsync(db, OutrasDespesas);
        await db.ContasPagar.Where(c => c.CategoriaFinanceiraId == null && c.NotaEntradaId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CategoriaFinanceiraId, fornecedores));
        // Lançadas à mão: reconhece pela descrição ("Aluguel da loja" → Aluguel); sem pista → Outras despesas.
        var semCategoria = await db.ContasPagar.Where(c => c.CategoriaFinanceiraId == null).ToListAsync();
        if (semCategoria.Count > 0)
        {
            var categorias = await db.CategoriasFinanceiras.AsNoTracking().ToListAsync();
            foreach (var c in semCategoria)
                c.CategoriaFinanceiraId = Financeiro.SugerirCategoria(c.Descricao, categorias, NaturezaFinanceira.Saida) ?? outras;
            await db.SaveChangesAsync();
        }

        var semMovimento = await db.ContasPagar
            .Where(c => c.PagaEm != null && !db.MovimentosFinanceiros.Any(m => m.ContaPagarId == c.Id)).ToListAsync();
        if (semMovimento.Count == 0) return;
        var banco = await Financeiro.ContaAsync(db, ContaBanco);
        foreach (var conta in semMovimento)
        {
            conta.ValorPago ??= conta.Valor;
            await Financeiro.RegistrarPagamentoAsync(db, conta, banco, conta.PagaPorId);
        }
        await db.SaveChangesAsync();
    }
}
