using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Marketplace.Api.Models.ChavesFinanceiras;

namespace Marketplace.Api.Services.Simulador;

// O PASSADO de uma LOJA em teste (TopSpin, esporte; Juvenal, mercado), para as telas e a DRE terem meses para analisar.
// Mesmas regras do bot ao vivo da loja:
//  • esporte (PerfilEsporte): poucos clientes, cesta pequena e cara, crédito parcelado, 45% identificados (os 60 fixos),
//    o vendedor ativo, e cada encordoamento vira uma OS já entregue;
//  • mercado (ComportamentoCliente/PerfilConsumo): o movimento do mercadinho, cesta do dia a dia, ~20% dão o CPF na nota
//    (os 60 clientes fixos do bairro), sem fiado (módulo desligado).
// Como o histórico da demonstração: NÃO mexe no estoque (Origem = Historico) e um caixa fechado por dia aberto.
// O financeiro começa no 1º dia do histórico (os saldos iniciais da implantação ficam): caixas lançados e depositados,
// maquininha, boletos dos fornecedores pelo custo vendido na semana, as contas fixas da loja (as mesmas da implantação,
// menos a mensalidade do Marcus, que ainda não era cliente) e o DAS — tudo pago no vencimento.
public class GeradorHistoricoLoja(AppDbContext db, ClimaService clima, LocaisDasLojas locais, UserManager<Usuario> usuarios,
    FinanceiroDemonstracao financeiro, IServiceScopeFactory escopos, ILogger<GeradorHistoricoLoja> log)
{
    // Sem o limite do estoque o passado venderia mais do que o bot ao vivo: 75% dá as ~6–7 vendas por dia útil combinadas
    // na loja de esporte. No mercado o estoque quase nunca falta inteiro (a cesta troca o item): o mesmo movimento do bot.
    private const double MovimentoEsporte = 0.75, MovimentoMercado = 1.0;

    public async Task<ResultadoHistorico> GerarAsync(DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var empresaId = db.EmpresaAtual;
        locais.Aplicar(empresaId); // relógio, cidade e horário de funcionamento desta loja
        var hoje = Relogio.Hoje;
        if (ate >= hoje) ate = hoje.AddDays(-1);
        if (de > ate || hoje.DayNumber - de.DayNumber > 90) throw new InvalidOperationException("Período inválido (até 90 dias para trás).");

        var (inicioUtc, fimUtc) = (Relogio.InicioDoDiaUtc(de), Relogio.InicioDoDiaUtc(ate.AddDays(1)));
        if (await db.Vendas.AnyAsync(v => v.DataHora >= inicioUtc && v.DataHora < fimUtc, ct))
            throw new InvalidOperationException("Já há vendas nesse período: o histórico só preenche dias vazios.");

        // O "operador" robô desta loja (o mesmo do bot ao vivo; criado se ainda não existe).
        var robo = await usuarios.FindByIdAsync(await SeedSimulador.GarantirRoboAsync(escopos, empresaId))
                   ?? throw new InvalidOperationException("Usuário do simulador não existe.");
        var local = locais.Local(empresaId) ?? throw new InvalidOperationException("Cadastre a cidade da loja antes (o clima vem de lá).");
        var climaPorHora = await clima.ObterHistoricoAsync(local, hoje.DayNumber - de.DayNumber + 1, ct);

        var esporte = await db.Empresas.AnyAsync(e => e.Id == empresaId && e.Ramo == Ramos.Esporte, ct);
        var movimento = esporte ? MovimentoEsporte : MovimentoMercado;
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.Modelo).Include(p => p.Categoria).Where(p => p.Ativo).ToListAsync(ct);
        var opcoes = esporte // sem limite de estoque no passado
            ? PerfilEsporte.Opcoes(produtos.Select(p => (p, 9999m)))
            : produtos.Select(p => (Produto: p, Habito: PerfilConsumo.Para(p), Disponivel: 9999m)).ToList();
        var chanceIdentificado = esporte ? ClientesSimulados.ChanceIdentificado : ClientesSimulados.ChanceIdentificadoMercado;
        var porId = produtos.ToDictionary(p => p.Id);
        var clientes = new List<Cliente>();
        for (var i = 0; i < ClientesSimulados.Quantos; i++) clientes.Add(await ClientesSimulados.SortearAsync(db, robo.Id, ct));
        clientes = await db.Clientes.Where(c => c.Simulado && c.Ativo).OrderBy(c => c.Id).ToListAsync(ct); // a turma inteira, em ordem
        foreach (var c in clientes.Where(c => c.CriadoEm > inicioUtc)) c.CriadoEm = inicioUtc; // clientes "desde" o começo do histórico
        await db.SaveChangesAsync(ct);
        var vendedores = await db.Vendedores.Where(v => v.Ativo).Select(v => v.Id).ToListAsync(ct);

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        int totalVendas = 0, totalOs = 0;
        decimal faturamento = 0;
        var novasOrdens = new List<(OrdemServico Ordem, Venda Venda)>();
        for (var dia = de; dia <= ate; dia = dia.AddDays(1))
        {
            if (Expediente.DoDia(dia) is not { } expediente) continue;
            var (abre, fecha) = (expediente.Abre.ToTimeSpan(), expediente.Fecha.ToTimeSpan());
            var sessao = new SessaoCaixa
            {
                NumeroCaixa = 1, UsuarioId = robo.Id, Status = StatusSessao.Fechada, ValorAbertura = 200,
                AbertaEm = Momento(dia, abre), FechadaEm = Momento(dia, fecha), ObservacaoFechamento = GeradorHistorico.Marca,
            };
            for (var hora = (int)abre.TotalHours; hora < Math.Ceiling(fecha.TotalHours); hora++)
            {
                var inicio = TimeSpan.FromHours(hora) < abre ? abre : TimeSpan.FromHours(hora);
                var termino = TimeSpan.FromHours(hora + 1) > fecha ? fecha : TimeSpan.FromHours(hora + 1);
                var tempo = climaPorHora.GetValueOrDefault(Momento(dia, TimeSpan.FromHours(hora))) ?? ClimaAgora.Neutro;
                var porHora = esporte ? PerfilEsporte.ClientesNaHora(dia, hora, tempo, movimento) : ComportamentoCliente.ClientesNaHora(dia, hora, tempo, movimento);
                var chegam = ComportamentoCliente.Poisson(porHora * (termino - inicio).TotalHours);
                for (var n = 0; n < chegam; n++)
                {
                    var momento = Momento(dia, inicio).AddSeconds(Random.Shared.Next((int)(termino - inicio).TotalSeconds));
                    var itens = ComportamentoCliente.MontarCesta(opcoes, tempo, momento.ToOffset(TimeSpan.FromHours(-3)), esporte ? PerfilEsporte.TamanhosDeCesta : null);
                    if (esporte) PerfilEsporte.CompletarCesta(itens, opcoes);
                    if (itens.Count == 0) continue;

                    var venda = new Venda { UsuarioId = robo.Id, DataHora = momento, Origem = OrigemVenda.Historico };
                    foreach (var item in itens.GroupBy(i => i.ProdutoId))
                    {
                        var p = porId[item.Key];
                        // Sem o limite do estoque, a cesta levaria 2–3 raquetes iguais: item caro (raquete, tênis, raqueteira) é 1.
                        var qtd = esporte && p.PrecoVenda >= 150 ? 1 : item.Sum(i => i.Quantidade);
                        venda.Itens.Add(new ItemVenda
                        {
                            ProdutoId = p.Id, Descricao = p.Nome, Unidade = p.Unidade, Quantidade = qtd, PrecoUnitario = p.PrecoVenda,
                            CustoUnitario = p.PrecoCusto, Total = Math.Round(qtd * p.PrecoVenda, 2, MidpointRounding.AwayFromZero),
                        });
                    }
                    venda.Subtotal = venda.Total = venda.Itens.Sum(i => i.Total);
                    var pg = esporte ? PerfilEsporte.Pagamento(venda.Total) : ComportamentoCliente.Pagamento(venda.Total);
                    venda.Pagamentos.Add(new PagamentoVenda { Forma = Enum.Parse<FormaPagamento>(pg.Forma), Valor = pg.Valor, Parcelas = pg.Parcelas });
                    venda.ValorPago = pg.Valor;
                    venda.Troco = pg.Valor - venda.Total;

                    // Cliente fixo (quem deixa raquete sempre se identifica) e o vendedor.
                    var servicos = venda.Itens.Select(i => porId[i.ProdutoId]).Where(p => p.Servico && OficinaDemonstracao.EhDaOficina(p.Nome)).ToList();
                    Cliente? cliente = servicos.Count > 0 || Random.Shared.NextDouble() < chanceIdentificado
                        ? (Random.Shared.NextDouble() < 0.65 ? clientes[Random.Shared.Next(Math.Min(20, clientes.Count))] : clientes[Random.Shared.Next(clientes.Count)])
                        : null;
                    venda.ClienteId = cliente?.Id;
                    venda.VendedorId = vendedores.Count == 0 ? null : vendedores[Random.Shared.Next(vendedores.Count)];
                    sessao.Vendas.Add(venda);

                    // A oficina: cada encordoamento/troca de grip vira uma OS (entregue em algumas horas a 1 dia).
                    var corda = venda.Itens.Select(i => porId[i.ProdutoId]).FirstOrDefault(p => p.Categoria?.Nome == "Cordas" || p.Nome.StartsWith("Corda "));
                    foreach (var s in servicos)
                    {
                        var encordoa = PerfilConsumo.SemAcento(s.Nome).Contains("encordoamento");
                        var pronta = momento.AddHours(PerfilConsumo.SemAcento(s.Nome).Contains("expresso") ? 1 : Random.Shared.Next(3, 22));
                        novasOrdens.Add((new OrdemServico
                        {
                            Cliente = cliente!.Nome, Telefone = cliente.Telefone, ClienteId = cliente.Id,
                            Raquete = cliente.Raquete ?? "Raquete do cliente", Corda = encordoa ? corda?.Nome ?? "Corda do cliente" : null,
                            Tensao = encordoa ? cliente.Tensao ?? "24/22 lbs" : null, Servico = s.Nome,
                            Valor = venda.Itens.Where(i => i.ProdutoId == s.Id || i.ProdutoId == corda?.Id).Sum(i => i.Total),
                            Paga = true, Status = StatusOrdem.Entregue, Automatica = true, CriadaPorId = robo.Id,
                            CriadaEm = momento, Prazo = pronta, ProntaEm = pronta, EntregueEm = pronta.AddHours(Random.Shared.Next(1, 30)),
                        }, venda));
                    }
                }
            }
            var dinheiro = sessao.Vendas.SelectMany(v => v.Pagamentos).Where(p => p.Forma == FormaPagamento.Dinheiro).Sum(p => p.Valor);
            sessao.ValorEsperado = sessao.ValorAbertura + dinheiro - sessao.Vendas.Sum(v => v.Troco);
            sessao.Diferenca = 0;
            sessao.ValorContado = sessao.ValorEsperado;
            db.SessoesCaixa.Add(sessao);
            await db.SaveChangesAsync(ct);
            totalVendas += sessao.Vendas.Count;
            faturamento += sessao.Vendas.Sum(v => v.Total);
        }

        // OS do passado: numeradas na ordem em que aconteceram, ANTES das que já existem (as de agora ganham número novo).
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        if (novasOrdens.Count > 0)
        {
            var existentes = await db.OrdensServico.OrderBy(o => o.Numero).ToListAsync(ct);
            foreach (var o in existentes) o.Numero += 100000; // abre espaço sem bater no índice único
            await db.SaveChangesAsync(ct);
            var n = 0;
            foreach (var (o, v) in novasOrdens.OrderBy(x => x.Ordem.CriadaEm)) { o.Numero = ++n; o.VendaId = v.Id; db.OrdensServico.Add(o); }
            foreach (var o in existentes) o.Numero = ++n;
            await db.SaveChangesAsync(ct);
            totalOs = novasOrdens.Count;
        }
        db.ChangeTracker.Clear();
        log.LogInformation("Histórico da loja {Empresa}: {De} a {Ate}, {Vendas} vendas, {Total:C}, {Os} OS.", empresaId, de, ate, totalVendas, faturamento, totalOs);

        // Financeiro desde o 1º dia (os saldos iniciais da implantação ficam como estão).
        foreach (var c in await db.ContasFinanceiras.Where(c => c.Chave != null).ToListAsync(ct)) c.SaldoInicialEm = de;
        await db.SaveChangesAsync(ct);
        var depositos = await financeiro.LancarCaixasEDepositarAsync(ct);
        var boletos = await financeiro.BoletosDoHistoricoAsync(de, ate, robo.Id);
        var fixas = await ContasFixasAsync(de, robo.Id, ct);
        var pagas = await financeiro.PagarVencidasAsync(hoje, robo.Id);
        var recebidos = await FinanceiroCaixa.LiquidarAsync(db, hoje);
        var resumo = $"{totalOs} OS, {depositos} depósitos, {boletos} boletos de fornecedor, {fixas} contas fixas/DAS, {pagas} contas pagas, {recebidos} recebíveis depositados";
        log.LogInformation("Financeiro do histórico da loja {Empresa}: {Resumo}.", empresaId, resumo);
        return new ResultadoHistorico(de, ate, ate.DayNumber - de.DayNumber + 1, totalVendas, faturamento, climaPorHora.Count, resumo);
    }

    // As contas fixas da loja nos meses do histórico: copiadas das da implantação (a 1ª parcela de cada uma), do 1º mês
    // do histórico até o mês antes delas começarem; e o DAS (alíquota da loja) de cada mês fechado. Marca da demonstração:
    // são pagas no vencimento e somem no "apagar histórico".
    private async Task<int> ContasFixasAsync(DateOnly de, string usuarioId, CancellationToken ct)
    {
        var modelos = (await db.ContasPagar.AsNoTracking()
                .Where(c => c.Observacao != FinanceiroDemonstracao.Marca && c.FornecedorId == null && c.Descricao.Contains("(1/"))
                .ToListAsync(ct))
            .Where(c => !c.Descricao.Contains("Marcus", StringComparison.OrdinalIgnoreCase))
            .Select(c => (Descricao: c.Descricao[..c.Descricao.LastIndexOf(" (")], c.Valor, Dia: c.Vencimento.Day, c.CategoriaFinanceiraId, Primeira: c.Vencimento))
            .ToList();
        var criadas = 0;
        for (var mes = new DateOnly(de.Year, de.Month, 1); mes <= Relogio.Hoje; mes = mes.AddMonths(1))
        {
            foreach (var m in modelos)
            {
                var venc = mes.AddDays(Math.Min(m.Dia, DateTime.DaysInMonth(mes.Year, mes.Month)) - 1);
                if (venc < de || venc >= m.Primeira) continue;
                criadas += Criar($"DEMO-FIXA-{m.Descricao.GetHashCode():x8}-{mes:yyyyMM}", $"{m.Descricao} · {mes:MM/yyyy}", venc, m.Valor, m.CategoriaFinanceiraId);
            }
            // DAS do mês anterior (vence dia 20), pela alíquota efetiva da loja.
            var referencia = mes.AddMonths(-1);
            if (referencia >= new DateOnly(de.Year, de.Month, 1))
            {
                var aliquota = await db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.AliquotaSimples).FirstAsync(ct);
                var (ini, fim) = (Relogio.InicioDoDiaUtc(referencia), Relogio.InicioDoDiaUtc(mes));
                var faturado = await db.Vendas.Where(v => v.Status == StatusVenda.Concluida && v.DataHora >= ini && v.DataHora < fim).SumAsync(v => (decimal?)v.Total, ct) ?? 0;
                var das = await db.CategoriasFinanceiras.Where(c => c.Grupo == GrupoFinanceiro.ImpostosVendas).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
                if (aliquota > 0 && faturado > 0)
                    criadas += Criar($"DEMO-FIXA-DAS-{mes:yyyyMM}", $"Simples Nacional (DAS) · competência {referencia:MM/yyyy}", mes.AddDays(19),
                        Math.Round(faturado * aliquota / 100m, 2), das);
            }
        }
        await db.SaveChangesAsync(ct);
        return criadas;

        int Criar(string documento, string descricao, DateOnly vencimento, decimal valor, int? categoria)
        {
            if (db.ContasPagar.Any(c => c.Documento == documento)) return 0;
            db.ContasPagar.Add(new ContaPagar
            {
                Descricao = descricao, Documento = documento, Vencimento = vencimento, Valor = valor, CategoriaFinanceiraId = categoria,
                Observacao = FinanceiroDemonstracao.Marca, CriadaPorId = usuarioId,
            });
            return 1;
        }
    }

    private static DateTimeOffset Momento(DateOnly dia, TimeSpan hora) => Relogio.InicioDoDiaUtc(dia).Add(hora);
}
