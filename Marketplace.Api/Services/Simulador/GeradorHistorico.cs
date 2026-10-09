using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Simulador;

public record ResultadoHistorico(DateOnly De, DateOnly Ate, int Dias, int Vendas, decimal Faturamento, int HorasDeClima);

// Gera o PASSADO do mercadinho: N dias de vendas, com o clima REAL de cada hora em Porto Alegre.
// Usa as mesmas regras do cliente ao vivo (ComportamentoCliente), mas:
//  • NÃO mexe no estoque (as vendas são marcadas com Origem = Historico);
//  • cria uma sessão do Caixa 9 por dia aberto (seg–sáb, 8h às 19h), já fechada.
public class GeradorHistorico(AppDbContext db, ClimaService clima, LocaisDasLojas locais, UserManager<Usuario> usuarios, ILogger<GeradorHistorico> log)
{
    public const string Marca = "Histórico gerado (simulador)";

    public async Task<ResultadoHistorico> GerarAsync(int dias, bool substituir, CancellationToken ct)
    {
        dias = Math.Clamp(dias, 1, 90);
        var hoje = Relogio.Hoje;
        var (de, ate) = (hoje.AddDays(-dias), hoje.AddDays(-1));
        var (inicioUtc, fimUtc) = (Relogio.InicioDoDiaUtc(de), Relogio.InicioDoDiaUtc(hoje));

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

        db.ChangeTracker.AutoDetectChangesEnabled = false; // inserção em lote: mais rápido
        int totalVendas = 0;
        decimal faturamento = 0;

        for (var dia = de; dia <= ate; dia = dia.AddDays(1))
        {
            if (!ComportamentoCliente.AbertoNoDia(dia)) continue; // domingo e feriado: fechado
            var (abre, fecha) = (ComportamentoCliente.Abre, ComportamentoCliente.Fecha);
            var sessao = new SessaoCaixa
            {
                NumeroCaixa = SimuladorEstado.NumeroCaixa,
                UsuarioId = robo.Id,
                Status = StatusSessao.Fechada,
                ValorAbertura = 200,
                AbertaEm = MomentoUtc(dia, abre, 0),
                FechadaEm = MomentoUtc(dia, fecha, 0),
                ObservacaoFechamento = Marca,
            };

            for (var hora = abre; hora < fecha; hora++)
            {
                var momentoHora = MomentoUtc(dia, hora, 0);
                var tempo = climaPorHora.GetValueOrDefault(momentoHora) ?? ClimaAgora.Neutro;
                var clientes = ComportamentoCliente.Poisson(ComportamentoCliente.ClientesNaHora(dia, hora, tempo, 1.0));

                for (var c = 0; c < clientes; c++)
                {
                    var momento = momentoHora.AddSeconds(Random.Shared.Next(3600));
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
                    sessao.Vendas.Add(venda);
                }
            }

            // Fechamento do dia: o esperado na gaveta, e 1 em cada 10 dias uma pequena diferença.
            var dinheiro = sessao.Vendas.SelectMany(v => v.Pagamentos).Where(p => p.Forma == FormaPagamento.Dinheiro).Sum(p => p.Valor);
            sessao.ValorEsperado = sessao.ValorAbertura + dinheiro - sessao.Vendas.Sum(v => v.Troco);
            sessao.Diferenca = Random.Shared.NextDouble() < 0.10 ? Math.Round((decimal)(Random.Shared.NextDouble() * 4 - 2), 2) : 0;
            sessao.ValorContado = sessao.ValorEsperado + sessao.Diferenca;

            db.SessoesCaixa.Add(sessao);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            totalVendas += sessao.Vendas.Count;
            faturamento += sessao.Vendas.Sum(v => v.Total);
        }

        log.LogInformation("Histórico gerado: {De} a {Ate}, {Vendas} vendas, {Total:C}.", de, ate, totalVendas, faturamento);
        return new ResultadoHistorico(de, ate, dias, totalVendas, faturamento, climaPorHora.Count);
    }

    // Apaga TODO o histórico gerado (vendas com Origem = Historico e as sessões marcadas). O resto fica intacto.
    public async Task<int> ApagarAsync(CancellationToken ct)
    {
        var vendas = await db.Vendas.Where(v => v.Origem == OrigemVenda.Historico).ExecuteDeleteAsync(ct); // itens e pagamentos vão junto (cascade)
        await db.SessoesCaixa.Where(s => s.ObservacaoFechamento == Marca && !s.Vendas.Any()).ExecuteDeleteAsync(ct);
        return vendas;
    }

    private static DateTimeOffset MomentoUtc(DateOnly dia, int hora, int minuto) =>
        Relogio.InicioDoDiaUtc(dia).AddHours(hora).AddMinutes(minuto);
}
