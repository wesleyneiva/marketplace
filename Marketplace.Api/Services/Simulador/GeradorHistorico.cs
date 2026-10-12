using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Simulador;

public record ResultadoHistorico(DateOnly De, DateOnly Ate, int Dias, int Vendas, decimal Faturamento, int HorasDeClima, string Financeiro);

// Gera o PASSADO do mercadinho: N dias de vendas, com o clima REAL de cada hora em Porto Alegre.
// Usa as mesmas regras do cliente ao vivo (ComportamentoCliente), mas:
//  • NÃO mexe no estoque (as vendas são marcadas com Origem = Historico);
//  • cria uma sessão do Caixa 9 por dia aberto (pelo horário de funcionamento da loja), já fechada.
public class GeradorHistorico(AppDbContext db, ClimaService clima, LocaisDasLojas locais, UserManager<Usuario> usuarios,
    FinanceiroDemonstracao financeiro, FiadoDemonstracao fiadoDemo, ILogger<GeradorHistorico> log)
{
    public const string Marca = "Histórico gerado (simulador)";

    public async Task<ResultadoHistorico> GerarAsync(int dias, bool substituir, CancellationToken ct)
    {
        dias = Math.Clamp(dias, 1, 90);
        var hoje = Relogio.Hoje;
        var (de, ate) = (hoje.AddDays(-dias), hoje.AddDays(-1));
        var (inicioUtc, fimUtc) = (Relogio.InicioDoDiaUtc(de), Relogio.InicioDoDiaUtc(hoje));

        // Dias que já têm vendas de verdade (PDV ou simulador ao vivo) não ganham histórico: senão o dia teria venda em dobro.
        var primeiraAoVivo = await db.Vendas.Where(v => v.Origem != OrigemVenda.Historico && v.DataHora >= inicioUtc && v.DataHora < fimUtc)
            .MinAsync(v => (DateTimeOffset?)v.DataHora, ct);
        if (primeiraAoVivo is { } primeira) ate = Relogio.DiaLocal(primeira).AddDays(-1);
        if (ate < de) throw new InvalidOperationException("Não há dias livres para gerar histórico nesse período (já há vendas de verdade).");

        if (await db.Vendas.AnyAsync(v => v.Origem == OrigemVenda.Historico && v.DataHora >= inicioUtc && v.DataHora < fimUtc, ct))
        {
            if (!substituir)
                throw new InvalidOperationException("Já existe histórico gerado nesse período. Use substituir=true para gerar de novo.");
            await ApagarAsync(ct);
        }

        var robo = await usuarios.FindByEmailAsync(SimuladorEstado.Email)
            ?? throw new InvalidOperationException("Usuário do simulador não existe.");
        var local = locais.Local(db.EmpresaAtual)
            ?? throw new InvalidOperationException("Cadastre a cidade da loja (Configurações) antes de gerar o histórico: o clima vem de lá.");
        var climaPorHora = await clima.ObterHistoricoAsync(local, dias + 1, ct);

        var produtos = await db.Produtos.AsNoTracking().Where(p => p.Ativo).ToListAsync(ct);
        // No histórico não há limite de estoque: "Disponivel" bem alto.
        var opcoes = produtos.Select(p => (Produto: p, Habito: PerfilConsumo.Para(p), Disponivel: 9999m)).ToList();
        var porId = produtos.ToDictionary(p => p.Id);

        // Caderneta: os clientes de demonstração e o que cada um está devendo, compra a compra (em memória, dia a dia).
        var clientesFiado = (await fiadoDemo.GarantirClientesAsync(robo.Id)).ToDictionary(c => c.Id);
        var cadernetas = clientesFiado.Keys.ToDictionary(id => id, _ => new List<CompraFiado>());
        int comprasFiado = 0, pagamentosFiado = 0, baixas = 0;

        db.ChangeTracker.AutoDetectChangesEnabled = false; // inserção em lote: mais rápido
        int totalVendas = 0;
        decimal faturamento = 0;

        for (var dia = de; dia <= ate; dia = dia.AddDays(1))
        {
            if (Expediente.DoDia(dia) is not { } expediente) continue; // dia fechado (domingo, feriado…)
            var (abre, fecha) = (expediente.Abre.ToTimeSpan(), expediente.Fecha.ToTimeSpan());
            var abreHora = (int)abre.TotalHours; // hora "cheia" em que abre (7:30 → 7)
            var sessao = new SessaoCaixa
            {
                NumeroCaixa = SimuladorEstado.NumeroCaixa,
                UsuarioId = robo.Id,
                Status = StatusSessao.Fechada,
                ValorAbertura = 200,
                AbertaEm = MomentoUtc(dia, abre),
                FechadaEm = MomentoUtc(dia, fecha),
                ObservacaoFechamento = Marca,
            };

            // Quem vem pagar o fiado hoje (chegou o "dia dele": vencimento + o jeito de cada um) e baixa do calote.
            // Os lançamentos da caderneta ficam numa lista e só entram no banco DEPOIS do caixa do dia estar completo:
            // com a detecção automática desligada (inserção em lote), registrar antes "congelaria" o caixa sem as vendas.
            decimal fiadoEmDinheiro = 0;
            var caderneta = new List<LancamentoFiado>();
            foreach (var (id, compras) in cadernetas)
            {
                var c = clientesFiado[id];
                var atraso = FiadoDemonstracao.DiasDepoisDoVencimento(c);
                if (atraso != int.MaxValue)
                {
                    var devidas = compras.Where(x => x.Aberto > 0 && x.Vencimento.AddDays(atraso) <= dia).ToList();
                    if (devidas.Count == 0) continue;
                    var forma = Random.Shared.NextDouble() < 0.6 ? FormaPagamento.Dinheiro : FormaPagamento.Pix;
                    var valor = devidas.Sum(x => x.Aberto);
                    devidas.ForEach(x => x.Aberto = 0);
                    if (forma == FormaPagamento.Dinheiro) fiadoEmDinheiro += valor;
                    caderneta.Add(new LancamentoFiado
                    {
                        ClienteId = id, Tipo = TipoLancamentoFiado.Pagamento, Valor = valor, Forma = forma, SessaoCaixa = sessao,
                        DataHora = MomentoUtc(dia, abre + (fecha - abre) * (0.1 + Random.Shared.NextDouble() * 0.8)), UsuarioId = robo.Id,
                        Observacao = $"Recebido no Caixa {SimuladorEstado.NumeroCaixa}",
                    });
                    pagamentosFiado++;
                }
                else if (FiadoDemonstracao.PerfilDe(c) == FiadoDemonstracao.Perfil.Calote)
                {
                    var caloteadas = compras.Where(x => x.Aberto > 0 && x.Vencimento.AddDays(FiadoDemonstracao.DiasParaBaixa) <= dia).ToList();
                    if (caloteadas.Count == 0) continue;
                    caderneta.Add(new LancamentoFiado
                    {
                        ClienteId = id, Tipo = TipoLancamentoFiado.Baixa, Valor = caloteadas.Sum(x => x.Aberto), UsuarioId = robo.Id,
                        DataHora = MomentoUtc(dia, abre + TimeSpan.FromHours(1)),
                        Observacao = $"Sem pagar há mais de {FiadoDemonstracao.DiasParaBaixa} dias e sem resposta: baixa (calote).",
                    });
                    caloteadas.ForEach(x => x.Aberto = 0);
                    baixas++;
                }
            }

            for (var hora = abreHora; hora < Math.Ceiling(fecha.TotalHours); hora++)
            {
                // A parte desta hora em que a loja está aberta (abre 7:30 → só meia hora às 7).
                var inicio = TimeSpan.FromHours(hora) < abre ? abre : TimeSpan.FromHours(hora);
                var termino = TimeSpan.FromHours(hora + 1) > fecha ? fecha : TimeSpan.FromHours(hora + 1);
                var momentoHora = MomentoUtc(dia, TimeSpan.FromHours(hora));
                var tempo = climaPorHora.GetValueOrDefault(momentoHora) ?? ClimaAgora.Neutro;
                var fracao = (termino - inicio).TotalHours;
                var clientes = ComportamentoCliente.Poisson(ComportamentoCliente.ClientesNaHora(dia, hora, tempo, 1.0) * fracao);

                for (var c = 0; c < clientes; c++)
                {
                    var momento = MomentoUtc(dia, inicio).AddSeconds(Random.Shared.Next((int)(termino - inicio).TotalSeconds));
                    var cesta = ComportamentoCliente.MontarCesta(opcoes, tempo, momento.ToOffset(TimeSpan.FromHours(-3)));
                    if (cesta.Count == 0) continue;

                    var venda = new Venda { UsuarioId = robo.Id, DataHora = momento, Origem = OrigemVenda.Historico };
                    foreach (var item in cesta)
                    {
                        var p = porId[item.ProdutoId];
                        venda.Itens.Add(new ItemVenda
                        {
                            ProdutoId = p.Id, Descricao = p.Nome, Unidade = p.Unidade, Quantidade = item.Quantidade,
                            PrecoUnitario = p.PrecoVenda, CustoUnitario = p.PrecoCusto,
                            Total = Math.Round(item.Quantidade * p.PrecoVenda, 2, MidpointRounding.AwayFromZero),
                        });
                    }
                    venda.Subtotal = venda.Total = venda.Itens.Sum(i => i.Total);
                    var pagamento = ComportamentoCliente.Pagamento(venda.Total);
                    venda.Pagamentos.Add(new PagamentoVenda { Forma = Enum.Parse<FormaPagamento>(pagamento.Forma), Valor = pagamento.Valor });
                    venda.ValorPago = pagamento.Valor;
                    venda.Troco = pagamento.Valor - venda.Total;

                    // ~3% no fiado: um cliente da caderneta que PODE comprar (regras do caixa, sem gerente).
                    if (Random.Shared.NextDouble() < FiadoDemonstracao.ChanceFiado
                        && EscolherFiado(clientesFiado, cadernetas, venda.Total, dia, de) is { } clienteId)
                    {
                        venda.Pagamentos.Clear();
                        venda.Pagamentos.Add(new PagamentoVenda { Forma = FormaPagamento.Fiado, Valor = venda.Total });
                        (venda.ValorPago, venda.Troco, venda.ClienteId) = (venda.Total, 0, clienteId);
                        var vencimento = FiadoService.Vencimento(dia, clientesFiado[clienteId].DiaPagamento);
                        caderneta.Add(new LancamentoFiado
                        {
                            ClienteId = clienteId, Tipo = TipoLancamentoFiado.Compra, Valor = venda.Total, Venda = venda,
                            Vencimento = vencimento, DataHora = momento, UsuarioId = robo.Id,
                        });
                        cadernetas[clienteId].Add(new CompraFiado(vencimento, venda.Total));
                        comprasFiado++;
                    }
                    sessao.Vendas.Add(venda);
                }
            }

            // Fechamento do dia: o esperado na gaveta, e 1 em cada 10 dias uma pequena diferença.
            var dinheiro = sessao.Vendas.SelectMany(v => v.Pagamentos).Where(p => p.Forma == FormaPagamento.Dinheiro).Sum(p => p.Valor);
            sessao.ValorEsperado = sessao.ValorAbertura + dinheiro - sessao.Vendas.Sum(v => v.Troco) + fiadoEmDinheiro;
            sessao.Diferenca = Random.Shared.NextDouble() < 0.10 ? Math.Round((decimal)(Random.Shared.NextDouble() * 4 - 2), 2) : 0;
            sessao.ValorContado = sessao.ValorEsperado + sessao.Diferenca;

            db.SessoesCaixa.Add(sessao);
            db.LancamentosFiado.AddRange(caderneta); // depois do caixa (ver acima)
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            totalVendas += sessao.Vendas.Count;
            faturamento += sessao.Vendas.Sum(v => v.Total);
        }

        log.LogInformation("Histórico gerado: {De} a {Ate}, {Vendas} vendas, {Total:C}; fiado: {Compras} compras, {Pagamentos} pagamentos, {Baixas} baixa(s).",
            de, ate, totalVendas, faturamento, comprasFiado, pagamentosFiado, baixas);

        // O dinheiro desse passado: caixas no Financeiro, depósitos, boletos, despesas fixas, pagamentos e maquininha.
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        db.ChangeTracker.Clear();
        await financeiro.ImplantarAsync(de);
        var depositos = await financeiro.LancarCaixasEDepositarAsync(ct);
        var boletos = await financeiro.BoletosDoHistoricoAsync(de, ate, robo.Id) + await financeiro.BoletosDosPedidosAsync(de, robo.Id);
        var fixas = await financeiro.ContasFixasAsync(new DateOnly(de.Year, de.Month, 1), hoje.AddDays(35), robo.Id); // o mês inteiro (as de antes do 1º dia são pagas nele)
        var pagas = await financeiro.PagarVencidasAsync(hoje, robo.Id);
        var recebidos = await FinanceiroCaixa.LiquidarAsync(db, hoje);
        var resumo = $"fiado: {comprasFiado} compras, {pagamentosFiado} pagamentos, {baixas} baixa(s); {depositos} depósitos, {boletos} boletos de fornecedor, {fixas} despesas fixas, {pagas} contas pagas, {recebidos} recebíveis depositados";
        log.LogInformation("Financeiro do histórico: {Resumo}.", resumo);
        return new ResultadoHistorico(de, ate, dias, totalVendas, faturamento, climaPorHora.Count, resumo);
    }

    // Apaga TODO o histórico gerado (vendas com Origem = Historico e as sessões marcadas). O resto fica intacto.
    public async Task<int> ApagarAsync(CancellationToken ct)
    {
        await fiadoDemo.ApagarHistoricoAsync(ct); // a caderneta aponta para as vendas e os caixas do histórico
        await financeiro.ApagarAsync(ct); // antes: os lançamentos apontam para as sessões
        var vendas = await db.Vendas.Where(v => v.Origem == OrigemVenda.Historico).ExecuteDeleteAsync(ct); // itens e pagamentos vão junto (cascade)
        await db.SessoesCaixa.Where(s => s.ObservacaoFechamento == Marca && !s.Vendas.Any()).ExecuteDeleteAsync(ct);
        return vendas;
    }

    // Uma compra no fiado, em memória: quanto ainda falta pagar dela.
    private sealed class CompraFiado(DateOnly vencimento, decimal valor)
    {
        public DateOnly Vencimento { get; } = vencimento;
        public decimal Aberto { get; set; } = valor;
    }

    // Quem pode levar no fiado agora: não bloqueado, dentro do limite, sem atraso grave. O calote só compra no começo.
    private static int? EscolherFiado(Dictionary<int, Cliente> clientes, Dictionary<int, List<CompraFiado>> cadernetas,
        decimal total, DateOnly dia, DateOnly inicio)
    {
        var podem = clientes.Values.Where(c =>
        {
            var perfil = FiadoDemonstracao.PerfilDe(c);
            if (perfil == FiadoDemonstracao.Perfil.Bloqueado || (perfil == FiadoDemonstracao.Perfil.Calote && dia > inicio.AddDays(20))) return false;
            var compras = cadernetas[c.Id];
            var atrasoGrave = compras.Any(x => x.Aberto > 0 && x.Vencimento.AddDays(FiadoService.DiasDeTolerancia) < dia);
            return !atrasoGrave && compras.Sum(x => x.Aberto) + total <= c.LimiteCredito;
        }).ToList();
        return podem.Count == 0 ? null : podem[Random.Shared.Next(podem.Count)].Id;
    }

    private static DateTimeOffset MomentoUtc(DateOnly dia, TimeSpan horaDoDia) =>
        Relogio.InicioDoDiaUtc(dia).Add(horaDoDia);
}
