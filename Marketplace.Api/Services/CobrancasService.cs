using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Cobranças da PLATAFORMA (o dono cobrando os mercados): gera as mensalidades, bloqueia quem atrasou demais e libera
// quem pagou. Tudo no calendário de Brasília. As tabelas não têm filtro de empresa (são da plataforma).
// Tudo automático (23/10/2026): cada fatura vira uma conta "Sistema Marcus" no Contas a pagar da LOJA; a loja dá baixa
// lá e a fatura fica Paga aqui (desfez lá, reabre aqui); não pagou, fica atrasada e segue o fluxo (aviso e bloqueio).
public class CobrancasService(AppDbContext db, IServiceProvider services, ILogger<CobrancasService> log)
{
    private static readonly string[] Meses = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    // Quanto o cliente paga por mês: valor combinado, ou plano + caixas além dos incluídos.
    public static decimal ValorMensal(Empresa e, Plano? p) =>
        e.PrecoCombinado ?? (p is null ? 0 : p.PrecoMensal + Math.Max(0, e.LimiteCaixas - p.CaixasIncluidos) * p.PrecoCaixaExtra);

    public static DateOnly VencimentoDe(Empresa e, DateOnly competencia) =>
        new(competencia.Year, competencia.Month, Math.Clamp(e.DiaVencimento, 1, 28));

    // Gera mensalidade para este cliente? (tem plano, não é cortesia, não é a plataforma, não foi suspenso à mão)
    public static bool Cobra(Empresa e) =>
        !e.Plataforma && !e.Cortesia && e.PlanoId is not null && (e.Ativa || e.MotivoSuspensao == MotivosSuspensao.Atraso);

    public static string Descricao(Empresa e, Plano? p, DateOnly competencia) =>
        $"{(p is null ? "Mensalidade" : "Plano " + p.Nome)} · {e.LimiteCaixas} caixa{(e.LimiteCaixas == 1 ? "" : "s")} · {Meses[competencia.Month - 1]}/{competencia.Year}"
        + (e.PrecoCombinado is not null ? " (valor combinado)" : "");

    public static int DiasDeAtraso(Fatura f, DateOnly hoje) =>
        f.Status == StatusFatura.Aberta && f.Vencimento < hoje ? hoje.DayNumber - f.Vencimento.DayNumber : 0;

    public async Task<ConfiguracaoPlataforma> ConfiguracaoAsync() =>
        await db.ConfiguracoesPlataforma.FirstOrDefaultAsync(c => c.Id == 1) ?? new ConfiguracaoPlataforma();

    // Mensalidades de cada mês desde o início do cliente (CobrarDesde, ou o cadastro) até o próximo mês, que vencem até
    // "hoje + dias de antecedência" e ainda não existem. Um mês que faltou (cliente cadastrado com início no passado)
    // também é gerado — no máximo 24 meses para trás.
    public async Task<int> GerarAsync(DateOnly hoje, int? soCliente = null)
    {
        var cfg = await ConfiguracaoAsync();
        var clientes = await db.Empresas.Include(e => e.Plano)
            .Where(e => !e.Plataforma && !e.Cortesia && e.PlanoId != null && (soCliente == null || e.Id == soCliente))
            .ToListAsync();
        var mesAtual = new DateOnly(hoje.Year, hoje.Month, 1);
        var existentes = await db.Faturas.Where(f => f.Mensalidade && f.Status != StatusFatura.Cancelada && f.Competencia >= mesAtual.AddMonths(-24))
            .Select(f => new { f.ClienteId, f.Competencia }).ToListAsync();

        var novas = 0;
        foreach (var e in clientes.Where(Cobra))
        {
            var inicio = e.CobrarDesde ?? DateOnly.FromDateTime(e.CriadoEm.UtcDateTime);
            var primeira = new DateOnly(inicio.Year, inicio.Month, 1);
            if (primeira < mesAtual.AddMonths(-24)) primeira = mesAtual.AddMonths(-24);
            for (var competencia = primeira; competencia <= mesAtual.AddMonths(1); competencia = competencia.AddMonths(1))
            {
                var vencimento = VencimentoDe(e, competencia);
                if (vencimento > hoje.AddDays(cfg.DiasGerarAntes)) continue;              // ainda é cedo
                if (e.CobrarDesde is { } desde && vencimento < desde) continue;          // antes do início combinado
                if (e.CobrarDesde is null && vencimento < DateOnly.FromDateTime(e.CriadoEm.UtcDateTime)) continue; // antes de ser cliente
                if (e.TesteAte is { } teste && vencimento <= teste) continue;            // teste grátis
                if (existentes.Any(x => x.ClienteId == e.Id && x.Competencia == competencia)) continue;
                db.Faturas.Add(new Fatura
                {
                    ClienteId = e.Id, Competencia = competencia, Vencimento = vencimento,
                    Valor = ValorMensal(e, e.Plano), Descricao = Descricao(e, e.Plano, competencia),
                });
                novas++;
            }
        }
        if (novas > 0)
        {
            await db.SaveChangesAsync();
            log.LogInformation("Cobranças: {Novas} mensalidade(s) gerada(s).", novas);
        }
        return novas;
    }

    // A próxima mensalidade de cada cliente que ainda não apareceu (é gerada sozinha "dias de antecedência" antes do
    // vencimento): para o dono saber o que vem aí. Mesmas regras do GerarAsync (início combinado, teste grátis).
    public async Task<List<ProximaMensalidade>> ProximasAsync(DateOnly hoje)
    {
        var cfg = await ConfiguracaoAsync();
        var clientes = await db.Empresas.AsNoTracking().Include(e => e.Plano)
            .Where(e => !e.Plataforma && !e.Cortesia && e.PlanoId != null).ToListAsync();
        var mesAtual = new DateOnly(hoje.Year, hoje.Month, 1);
        var existentes = await db.Faturas.Where(f => f.Mensalidade && f.Status != StatusFatura.Cancelada && f.Competencia >= mesAtual.AddMonths(-1))
            .Select(f => new { f.ClienteId, f.Competencia }).ToListAsync();
        var proximas = new List<ProximaMensalidade>();
        foreach (var e in clientes.Where(Cobra))
        {
            for (var competencia = mesAtual.AddMonths(-1); competencia <= mesAtual.AddMonths(24); competencia = competencia.AddMonths(1))
            {
                var vencimento = VencimentoDe(e, competencia);
                if (vencimento <= hoje.AddDays(cfg.DiasGerarAntes)) continue;            // já era para ter aparecido
                if (e.CobrarDesde is { } desde && vencimento < desde) continue;
                if (e.CobrarDesde is null && vencimento < DateOnly.FromDateTime(e.CriadoEm.UtcDateTime)) continue;
                if (e.TesteAte is { } teste && vencimento <= teste) continue;
                if (existentes.Any(x => x.ClienteId == e.Id && x.Competencia == competencia)) continue;
                proximas.Add(new ProximaMensalidade(e.Id, e.Nome, competencia, vencimento, ValorMensal(e, e.Plano),
                    vencimento.AddDays(-cfg.DiasGerarAntes)));
                break;
            }
        }
        return proximas.OrderBy(p => p.Vencimento).ThenBy(p => p.Cliente).ToList();
    }

    // ================================================================ a conta da loja

    // Cada fatura tem a sua conta no Contas a pagar da loja (cria ou acha a que a loja já lançou: "Sistema Marcus…" com
    // vencimento no mesmo mês) e as duas andam juntas: a loja pagou → Paga aqui (e libera quem estava bloqueado por
    // atraso); desfez lá → Aberta de novo (só se foi a loja que pagou); valor/vencimento mudados aqui → a conta em aberto
    // acompanha; fatura cancelada → a conta em aberto sai. A loja apagou a conta → ela volta na próxima rodada.
    public async Task<int> SincronizarAsync(DateOnly hoje, int? soCliente = null)
    {
        var faturas = await db.Faturas.Include(f => f.Cliente)
            .Where(f => (soCliente == null || f.ClienteId == soCliente) && !f.Cliente!.Plataforma).ToListAsync();
        var mudancas = 0;
        var pagas = new HashSet<int>();
        foreach (var doCliente in faturas.GroupBy(f => f.ClienteId))
        {
            using var escopo = services.CriarEscopo(doCliente.Key);
            var loja = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = doCliente.Select(f => f.Id).ToList();
            var ligadas = await loja.ContasPagar.Where(c => c.FaturaId != null && ids.Contains(c.FaturaId.Value)).ToListAsync();
            int? categoria = null;
            foreach (var f in doCliente.OrderBy(f => f.Vencimento))
            {
                var conta = ligadas.FirstOrDefault(c => c.FaturaId == f.Id);
                if (f.Status == StatusFatura.Cancelada)
                {
                    if (conta is { PagaEm: null }) { loja.ContasPagar.Remove(conta); mudancas++; }
                    continue;
                }
                if (conta is null)
                {
                    // A loja já tinha lançado a mensalidade (ex.: as 12 parcelas da implantação)? Usa essa.
                    var (ini, fim) = (new DateOnly(f.Vencimento.Year, f.Vencimento.Month, 1), new DateOnly(f.Vencimento.Year, f.Vencimento.Month, 1).AddMonths(1));
                    conta = await loja.ContasPagar.Where(c => c.FaturaId == null && c.Descricao.StartsWith("Sistema Marcus")
                            && c.Vencimento >= ini && c.Vencimento < fim)
                        .OrderBy(c => c.Vencimento).FirstOrDefaultAsync();
                    if (conta is null)
                    {
                        categoria ??= await loja.CategoriasFinanceiras.Where(c => c.Nome == "Sistemas e software").Select(c => (int?)c.Id).FirstOrDefaultAsync() ?? 0;
                        conta = new ContaPagar
                        {
                            Descricao = Cortar($"Sistema Marcus · {f.Descricao}", 200), Vencimento = f.Vencimento, Valor = f.Valor,
                            CategoriaFinanceiraId = categoria > 0 ? categoria : null,
                            Observacao = f.Status == StatusFatura.Paga
                                ? $"Mensalidade do Marcus já recebida pela WN Labs em {f.PagaEm:dd/MM/yyyy}: dê baixa para registrar a saída do dinheiro."
                                : "Mensalidade do Marcus (lançada sozinha pela WN Labs). Dê baixa aqui quando pagar.",
                        };
                        loja.ContasPagar.Add(conta);
                    }
                    conta.FaturaId = f.Id;
                    ligadas.Add(conta);
                    mudancas++;
                }
                if (conta.PagaEm is { } pagaEm && f.Status == StatusFatura.Aberta)
                {
                    (f.Status, f.PagaEm, f.ValorPago, f.FormaPagamento, f.PagaPelaLoja) = (StatusFatura.Paga, pagaEm, conta.ValorPago ?? conta.Valor, Forma(conta.FormaPagamento), true);
                    f.Observacao ??= "Baixa no Contas a pagar da loja.";
                    pagas.Add(f.ClienteId);
                    mudancas++;
                }
                else if (conta.PagaEm is null && f.Status == StatusFatura.Paga && f.PagaPelaLoja)
                {
                    (f.Status, f.PagaEm, f.ValorPago, f.FormaPagamento, f.PagaPelaLoja) = (StatusFatura.Aberta, null, null, null, false);
                    mudancas++;
                }
                else if (conta.PagaEm is null && f.Status == StatusFatura.Aberta && (conta.Valor != f.Valor || conta.Vencimento != f.Vencimento))
                {
                    (conta.Valor, conta.Vencimento) = (f.Valor, f.Vencimento);
                    mudancas++;
                }
            }
            await loja.SaveChangesAsync();
        }
        await db.SaveChangesAsync();
        foreach (var id in pagas) await LiberarSeEmDiaAsync(id, hoje);
        if (mudancas > 0) log.LogInformation("Cobranças: {Mudancas} acerto(s) entre faturas e contas das lojas.", mudancas);
        return mudancas;
    }

    // "Pix", "Boleto", "TED"… do Contas a pagar da loja → a forma da fatura (null quando não dá para saber).
    private static string? Forma(string? texto) => Normal(texto) switch
    {
        var t when t.Contains("pix") => "Pix",
        var t when t.Contains("boleto") => "Boleto",
        var t when t.Contains("transf") || t.Contains("ted") || t.Contains("doc") => "Transferencia",
        var t when t.Contains("dinheiro") || t.Contains("especie") => "Dinheiro",
        var t when t.Contains("cart") || t.Contains("credito") || t.Contains("debito") => "Cartao",
        _ => null,
    };
    private static string Normal(string? t) => ImportacaoProdutosService.Normalizar(t ?? "");
    private static string Cortar(string v, int max) => v.Length <= max ? v : v[..max];

    // Bloqueio automático: mensalidade aberta vencida há mais de X dias.
    public async Task<int> BloquearAtrasadosAsync(DateOnly hoje)
    {
        var cfg = await ConfiguracaoAsync();
        if (!cfg.BloqueioAutomatico) return 0;
        var limite = hoje.AddDays(-cfg.DiasParaBloquear);
        var ids = await db.Faturas.Where(f => f.Status == StatusFatura.Aberta && f.Vencimento < limite)
            .Select(f => f.ClienteId).Distinct().ToListAsync();
        var bloquear = await db.Empresas.Where(e => ids.Contains(e.Id) && e.Ativa && !e.Cortesia && !e.Plataforma).ToListAsync();
        foreach (var e in bloquear)
        {
            await SuspenderAsync(e, MotivosSuspensao.Atraso);
            log.LogWarning("Cobranças: {Cliente} (#{Id}) bloqueado por atraso.", e.Nome, e.Id);
        }
        return bloquear.Count;
    }

    // Quem foi bloqueado por ATRASO volta sozinho quando não tem mais nada vencido além da tolerância.
    public async Task<bool> LiberarSeEmDiaAsync(int clienteId, DateOnly hoje)
    {
        var e = await db.Empresas.FirstAsync(x => x.Id == clienteId);
        if (e.Ativa || e.MotivoSuspensao != MotivosSuspensao.Atraso) return false;
        var cfg = await ConfiguracaoAsync();
        var limite = hoje.AddDays(-cfg.DiasParaBloquear);
        if (await db.Faturas.AnyAsync(f => f.ClienteId == clienteId && f.Status == StatusFatura.Aberta && f.Vencimento < limite)) return false;
        await ReativarAsync(e);
        log.LogInformation("Cobranças: {Cliente} (#{Id}) liberado (pagamento em dia).", e.Nome, e.Id);
        return true;
    }

    // Suspende: ninguém da empresa entra e quem está logado sai em até 1 minuto (troca o "carimbo de segurança").
    public async Task SuspenderAsync(Empresa e, string motivo)
    {
        e.Ativa = false;
        e.MotivoSuspensao = motivo;
        e.SuspensaEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        using var escopo = services.CriarEscopo(e.Id);
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        foreach (var u in await escopo.ServiceProvider.GetRequiredService<AppDbContext>().Users.ToListAsync())
            await usuarios.UpdateSecurityStampAsync(u);
    }

    public async Task ReativarAsync(Empresa e)
    {
        e.Ativa = true;
        e.MotivoSuspensao = null;
        e.SuspensaEm = null;
        await db.SaveChangesAsync();
    }
}

// Roda sozinho: 1 minuto depois de ligar e a cada hora — gera as mensalidades e aplica o bloqueio por atraso.
public class CobrancasTrabalhador(IServiceScopeFactory escopos, ILogger<CobrancasTrabalhador> log) : BackgroundService
{
    public static DateTimeOffset? UltimaRodada { get; private set; } // para a tela Sistema

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), parar);
        using var relogio = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                Relogio.UsarFuso(null); // calendário da plataforma: Brasília
                using var scope = escopos.CreateScope();
                var cobrancas = scope.ServiceProvider.GetRequiredService<CobrancasService>();
                await cobrancas.GerarAsync(Relogio.Hoje);
                await cobrancas.SincronizarAsync(Relogio.Hoje); // antes do bloqueio: quem pagou na loja não é bloqueado
                await cobrancas.BloquearAtrasadosAsync(Relogio.Hoje);
                UltimaRodada = DateTimeOffset.UtcNow;
            }
            catch (Exception e) when (!parar.IsCancellationRequested)
            {
                log.LogError(e, "Erro na rotina de cobranças");
            }
        } while (await relogio.WaitForNextTickAsync(parar));
    }
}
