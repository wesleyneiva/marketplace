using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;
using static Marketplace.Api.Models.ChavesFinanceiras;

namespace Marketplace.Api.Services.Simulador;

// O dinheiro do mercadinho de DEMONSTRAÇÃO (fictício), para o Financeiro e a DRE terem o que mostrar:
//  • depósito diário do dinheiro da gaveta no banco (fica o troco);
//  • despesas fixas do mês (aluguel, folha, energia, Simples...) como contas a pagar, pagas no vencimento;
//  • boletos dos fornecedores (no histórico: pelo custo do que foi vendido na semana; ao vivo: pelos pedidos recebidos).
// Números calibrados com a demonstração (~R$ 130 mil/mês, margem bruta ~35%) para um lucro de 4–5%, o normal do varejo.
// Tudo que é criado aqui leva uma MARCA, para o "apagar histórico" desfazer sem tocar no que pessoas lançaram.
public class FinanceiroDemonstracao(AppDbContext db, ILogger<FinanceiroDemonstracao> log)
{
    public const string Marca = "Demonstração (simulador)";      // Observacao das contas a pagar geradas
    private const string DocDeposito = "DEMO-DEP-";               // Documento do depósito: DEMO-DEP-<sessão>
    public const decimal TrocoDaGaveta = 200m;
    public const decimal AliquotaSimples = 7.3m;                   // % do faturamento do mês anterior (Anexo I, faixa típica)

    // Despesas fixas: (chave, descrição, dia do vencimento, categoria padrão, valor do mês).
    private static readonly (string Chave, string Descricao, int Dia, string Categoria, Func<int, int, decimal> Valor)[] Fixas =
    [
        ("ALUGUEL", "Aluguel da loja", 5, "Aluguel", (_, _) => 6000m),
        ("FOLHA", "Folha de pagamento (4 funcionários)", 5, "Salários", (_, _) => 8400m),
        ("BENEFICIOS", "Vale-transporte e vale-alimentação", 5, "Benefícios (VT/VA)", (_, _) => 1400m),
        ("PROLABORE", "Pró-labore do sócio", 5, "Pró-labore", (_, _) => 5000m),
        ("FGTS", "FGTS dos funcionários", 7, "Encargos (INSS/FGTS)", (_, _) => 680m),
        ("INSS", "INSS do pró-labore (GPS)", 20, "Encargos (INSS/FGTS)", (_, _) => 550m),
        // Energia: freezers e balcões refrigerados — mais cara nos meses quentes.
        ("ENERGIA", "Energia elétrica (CEEE)", 15, "Energia elétrica", (_, m) => m is 12 or 1 or 2 or 3 ? 3950m : m is 6 or 7 or 8 ? 2980m : 3380m),
        ("AGUA", "Água e esgoto (DMAE)", 12, "Água e esgoto", (_, _) => 240m),
        ("INTERNET", "Internet e telefone", 18, "Internet e telefone", (_, _) => 189.90m),
        ("CONTADOR", "Honorários do contador", 10, "Contabilidade", (_, _) => 950m),
        ("SISTEMA", "Sistema Marketplace (mensalidade)", 10, "Sistemas e software", (_, _) => 199m),
        ("IPTU", "IPTU (parcela)", 10, "IPTU e condomínio", (_, _) => 180m),
        ("TARIFA", "Tarifa do pacote de serviços do banco", 1, "Tarifas bancárias", (_, _) => 59.90m),
        ("SACOLAS", "Sacolas e bobinas", 25, "Sacolas e embalagens", (_, _) => 420m),
        ("REFRIGERACAO", "Manutenção preventiva da refrigeração", 22, "Manutenção e reparos", (_, _) => 280m),
        ("PANFLETOS", "Panfletos de ofertas", 3, "Marketing e divulgação", (_, _) => 300m),
    ];

    // Implantação da demonstração: o financeiro "começa" no primeiro dia do histórico.
    public async Task ImplantarAsync(DateOnly desde)
    {
        var contas = await db.ContasFinanceiras.Where(c => c.Chave != null).ToListAsync();
        foreach (var c in contas)
        {
            c.SaldoInicialEm = desde;
            c.SaldoInicial = c.Chave switch { ContaBanco => 25000m, ContaDinheiro => TrocoDaGaveta, _ => 0m };
        }
        await db.SaveChangesAsync();
    }

    // Caixas fechados ainda não lançados → Financeiro; e o depósito do dia (fica o troco na gaveta).
    public async Task<int> LancarCaixasEDepositarAsync(CancellationToken ct = default)
    {
        var desde = await db.ContasFinanceiras.Where(c => c.Chave == ContaDinheiro).Select(c => c.SaldoInicialEm).FirstAsync(ct);
        var sessoes = await db.SessoesCaixa.Where(s => s.Status == StatusSessao.Fechada && s.AbertaEm >= Relogio.InicioDoDiaUtc(desde))
            .OrderBy(s => s.AbertaEm).ToListAsync(ct);
        var depositos = 0;
        foreach (var sessao in sessoes)
        {
            await FinanceiroCaixa.PrepararFechamentoAsync(db, sessao); // não faz nada se já lançado
            if (await DepositarAsync(sessao)) depositos++;
            await db.SaveChangesAsync(ct);
        }
        return depositos;
    }

    // Leva ao banco o dinheiro contado no fechamento, menos o troco que fica para amanhã. Uma vez por caixa.
    public async Task<bool> DepositarAsync(SessaoCaixa sessao)
    {
        if (sessao.LancadaNoFinanceiroEm is null || sessao.ValorContado is not decimal contado) return false;
        var documento = DocDeposito + sessao.Id;
        if (await db.MovimentosFinanceiros.AnyAsync(m => m.Documento == documento)) return false;
        var valor = contado - Math.Min(TrocoDaGaveta, contado);
        if (valor <= 0) return false;

        var gaveta = await Financeiro.ContaAsync(db, ContaDinheiro);
        var banco = await Financeiro.ContaAsync(db, ContaBanco);
        var dia = Relogio.DiaLocal(sessao.AbertaEm);
        var guid = Guid.NewGuid();
        foreach (var (tipo, conta) in new[] { (TipoMovimentoFinanceiro.Saida, gaveta), (TipoMovimentoFinanceiro.Entrada, banco) })
            db.MovimentosFinanceiros.Add(new MovimentoFinanceiro
            {
                Data = dia, Tipo = tipo, Valor = valor, ContaFinanceiraId = conta, Documento = documento, TransferenciaId = guid,
                Descricao = $"Depósito do dinheiro do Caixa {sessao.NumeroCaixa} (fica {TrocoDaGaveta:C0} de troco)",
                Origem = OrigemMovimentoFinanceiro.Transferencia, CriadoPorId = sessao.UsuarioId,
            });
        return true;
    }

    // Despesas fixas com vencimento entre "de" e "ate" (não cria duas vezes). O DAS usa o faturamento do mês anterior,
    // e só nasce quando aquele mês acabou.
    public async Task<int> ContasFixasAsync(DateOnly de, DateOnly ate, string? usuarioId)
    {
        var hoje = Relogio.Hoje;
        var existentes = (await db.ContasPagar.Where(c => c.Documento != null && c.Documento.StartsWith("DEMO-FIXA-"))
            .Select(c => c.Documento!).ToListAsync()).ToHashSet();
        var categorias = await db.CategoriasFinanceiras.AsNoTracking().ToListAsync();
        var outras = await Financeiro.CategoriaAsync(db, OutrasDespesas);
        int Categoria(string nome) => categorias.FirstOrDefault(c => c.Nome == nome)?.Id ?? outras;

        var criadas = 0;
        for (var mes = new DateOnly(de.Year, de.Month, 1); mes <= ate; mes = mes.AddMonths(1))
        {
            foreach (var f in Fixas)
                criadas += Criar($"{f.Chave}-{mes:yyyy-MM}", $"{f.Descricao} · {Mes(mes)}", mes.AddDays(f.Dia - 1), f.Valor(mes.Year, mes.Month), Categoria(f.Categoria));

            // Simples Nacional do mês anterior (vence dia 20).
            var referencia = mes.AddMonths(-1);
            if (referencia < new DateOnly(hoje.Year, hoje.Month, 1))
            {
                var (ini, fim) = (Relogio.InicioDoDiaUtc(referencia), Relogio.InicioDoDiaUtc(mes));
                var faturamento = await db.Vendas.Where(v => v.Status == StatusVenda.Concluida && v.DataHora >= ini && v.DataHora < fim)
                    .SumAsync(v => (decimal?)v.Total) ?? 0;
                if (faturamento > 0)
                    criadas += Criar($"DAS-{mes:yyyy-MM}", $"Simples Nacional (DAS) · competência {Mes(referencia)}", mes.AddDays(19),
                        Math.Round(faturamento * AliquotaSimples / 100m, 2), Categoria("Simples Nacional (DAS)"));
            }
        }
        await db.SaveChangesAsync();
        return criadas;

        int Criar(string chave, string descricao, DateOnly vencimento, decimal valor, int categoria)
        {
            var documento = "DEMO-FIXA-" + chave;
            if (vencimento < de || vencimento > ate || existentes.Contains(documento)) return 0;
            existentes.Add(documento);
            db.ContasPagar.Add(new ContaPagar
            {
                Descricao = descricao, Documento = documento, Vencimento = vencimento, Valor = valor,
                CategoriaFinanceiraId = categoria, Observacao = Marca, CriadaPorId = usuarioId,
            });
            return 1;
        }
    }

    // HISTÓRICO: boletos semanais de cada fornecedor = custo do que foi vendido na semana (o estoque "girou").
    // Prazo de pagamento conforme o fornecedor: entrega diária paga em 7 dias; a cada 4 dias, em 28.
    public async Task<int> BoletosDoHistoricoAsync(DateOnly de, DateOnly ate, string? usuarioId)
    {
        var (ini, fim) = (Relogio.InicioDoDiaUtc(de), Relogio.InicioDoDiaUtc(ate.AddDays(1)));
        var custos = await db.ItensVenda.AsNoTracking()
            .Where(i => i.Venda!.Origem == OrigemVenda.Historico && i.Venda.Status == StatusVenda.Concluida
                        && i.Venda.DataHora >= ini && i.Venda.DataHora < fim && i.Produto!.FornecedorId != null)
            .Select(i => new { i.Venda!.DataHora, FornecedorId = i.Produto!.FornecedorId!.Value, Custo = i.CustoUnitario * i.Quantidade })
            .ToListAsync();
        var fornecedores = await db.Fornecedores.AsNoTracking().ToDictionaryAsync(f => f.Id);
        var categoria = await Financeiro.CategoriaAsync(db, Fornecedores);
        var existentes = (await db.ContasPagar.Where(c => c.Documento != null && c.Documento.StartsWith("DEMO-SEM-"))
            .Select(c => c.Documento!).ToListAsync()).ToHashSet();

        var criadas = 0;
        foreach (var g in custos.GroupBy(c => (Semana: Segunda(Relogio.DiaLocal(c.DataHora)), c.FornecedorId)))
        {
            var documento = $"DEMO-SEM-{g.Key.Semana:yyyyMMdd}-{g.Key.FornecedorId}";
            if (existentes.Contains(documento)) continue;
            var f = fornecedores[g.Key.FornecedorId];
            db.ContasPagar.Add(new ContaPagar
            {
                Descricao = $"Compras da semana de {g.Key.Semana:dd/MM} — {f.Nome}", Documento = documento,
                Vencimento = g.Key.Semana.AddDays(PrazoPagamento(f)), Valor = Math.Round(g.Sum(c => c.Custo), 2),
                FornecedorId = f.Id, CategoriaFinanceiraId = categoria, Observacao = Marca, CriadaPorId = usuarioId,
            });
            criadas++;
        }
        await db.SaveChangesAsync();
        return criadas;
    }

    // AO VIVO: pedido automático recebido → boleto do fornecedor (uma vez por pedido).
    public async Task<int> BoletosDosPedidosAsync(DateOnly desde, string? usuarioId)
    {
        var existentes = (await db.ContasPagar.Where(c => c.Documento != null && c.Documento.StartsWith("DEMO-PED-"))
            .Select(c => c.Documento!).ToListAsync()).ToHashSet();
        var pedidos = await db.PedidosCompra.AsNoTracking().Include(p => p.Fornecedor)
            .Where(p => p.Automatico && p.Status == StatusPedido.Recebido && p.RecebidoEm >= Relogio.InicioDoDiaUtc(desde))
            .Select(p => new { p.Id, p.Fornecedor, p.RecebidoEm, Valor = p.Itens.Sum(i => (i.QuantidadeRecebida ?? i.Quantidade) * i.CustoUnitario) })
            .ToListAsync();
        var categoria = await Financeiro.CategoriaAsync(db, Fornecedores);
        var criadas = 0;
        foreach (var p in pedidos.Where(p => p.Valor > 0 && !existentes.Contains($"DEMO-PED-{p.Id}")))
        {
            var recebido = Relogio.DiaLocal(p.RecebidoEm!.Value);
            db.ContasPagar.Add(new ContaPagar
            {
                Descricao = $"Pedido nº {p.Id} — {p.Fornecedor!.Nome} (boleto)", Documento = $"DEMO-PED-{p.Id}",
                Vencimento = recebido.AddDays(PrazoPagamento(p.Fornecedor)), Valor = Math.Round(p.Valor, 2),
                FornecedorId = p.Fornecedor.Id, CategoriaFinanceiraId = categoria, Observacao = Marca, CriadaPorId = usuarioId,
            });
            criadas++;
        }
        await db.SaveChangesAsync();
        return criadas;
    }

    // Paga no vencimento (pelo banco) as contas geradas para a demonstração. As lançadas por pessoas, não toca.
    public async Task<int> PagarVencidasAsync(DateOnly hoje, string? usuarioId)
    {
        var banco = await db.ContasFinanceiras.FirstAsync(c => c.Chave == ContaBanco);
        var contas = await db.ContasPagar.Where(c => c.Observacao == Marca && c.PagaEm == null && c.Vencimento <= hoje)
            .OrderBy(c => c.Vencimento).ThenBy(c => c.Id).ToListAsync();
        foreach (var c in contas)
        {
            c.PagaEm = c.Vencimento < banco.SaldoInicialEm ? banco.SaldoInicialEm : c.Vencimento;
            c.ValorPago = c.Valor;
            c.FormaPagamento = c.FornecedorId is not null ? "Boleto" : "Débito automático";
            c.PagaPorId = usuarioId;
            await Financeiro.RegistrarPagamentoAsync(db, c, banco.Id, usuarioId);
        }
        await db.SaveChangesAsync();
        return contas.Count;
    }

    // Desfaz TUDO o que este gerador criou (e o lançamento dos caixas do histórico), sem tocar no que pessoas lançaram.
    public async Task ApagarAsync(CancellationToken ct = default)
    {
        var contas = db.ContasPagar.Where(c => c.Observacao == Marca);
        await db.MovimentosFinanceiros.Where(m => m.ContaPagarId != null && contas.Any(c => c.Id == m.ContaPagarId)).ExecuteDeleteAsync(ct);
        await contas.ExecuteDeleteAsync(ct);
        await db.MovimentosFinanceiros.Where(m => m.Documento != null && m.Documento.StartsWith(DocDeposito)).ExecuteDeleteAsync(ct);

        // Caixas do histórico: lançamentos, recebíveis e os depósitos da maquininha deles. Um depósito pode misturar
        // caixas do histórico e do simulador ao vivo: os recebíveis ao vivo voltam a "previsto" e caem de novo depois.
        var historicas = db.SessoesCaixa.Where(s => s.ObservacaoFechamento == GeradorHistorico.Marca);
        var liquidacoes = await db.Recebiveis.Where(r => historicas.Any(s => s.Id == r.SessaoCaixaId) && r.LiquidacaoId != null)
            .Select(r => r.LiquidacaoId!.Value).Distinct().ToListAsync(ct);
        await db.MovimentosFinanceiros.Where(m => m.LiquidacaoId != null && liquidacoes.Contains(m.LiquidacaoId.Value)).ExecuteDeleteAsync(ct);
        await db.Recebiveis.Where(r => r.LiquidacaoId != null && liquidacoes.Contains(r.LiquidacaoId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StatusRecebivel.Previsto).SetProperty(r => r.RecebidoEm, (DateOnly?)null)
                .SetProperty(r => r.LiquidacaoId, (Guid?)null), ct);
        await db.Recebiveis.Where(r => historicas.Any(s => s.Id == r.SessaoCaixaId)).ExecuteDeleteAsync(ct);
        await db.MovimentosFinanceiros.Where(m => m.SessaoCaixaId != null && historicas.Any(s => s.Id == m.SessaoCaixaId)).ExecuteDeleteAsync(ct);
        await historicas.ExecuteUpdateAsync(s => s.SetProperty(x => x.LancadaNoFinanceiroEm, (DateTimeOffset?)null), ct);
        log.LogInformation("Financeiro da demonstração apagado.");
    }

    private static int PrazoPagamento(Fornecedor f) => Math.Clamp(f.PrazoEntregaDias, 1, 4) * 7;
    private static DateOnly Segunda(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static readonly string[] Meses = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];
    private static string Mes(DateOnly d) => $"{Meses[d.Month - 1]}/{d.Year}";
}
