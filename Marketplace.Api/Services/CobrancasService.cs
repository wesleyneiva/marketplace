using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Cobranças da PLATAFORMA (o dono cobrando os mercados): gera as mensalidades, bloqueia quem atrasou demais e libera
// quem pagou. Tudo no calendário de Brasília. As tabelas não têm filtro de empresa (são da plataforma).
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

    // Mensalidades do mês atual e do próximo que vencem até "hoje + dias de antecedência" e ainda não existem.
    public async Task<int> GerarAsync(DateOnly hoje, int? soCliente = null)
    {
        var cfg = await ConfiguracaoAsync();
        var clientes = await db.Empresas.Include(e => e.Plano)
            .Where(e => !e.Plataforma && !e.Cortesia && e.PlanoId != null && (soCliente == null || e.Id == soCliente))
            .ToListAsync();
        var mesAtual = new DateOnly(hoje.Year, hoje.Month, 1);
        var existentes = await db.Faturas.Where(f => f.Status != StatusFatura.Cancelada && f.Competencia >= mesAtual.AddMonths(-1))
            .Select(f => new { f.ClienteId, f.Competencia }).ToListAsync();

        var novas = 0;
        foreach (var e in clientes.Where(Cobra))
        {
            foreach (var competencia in new[] { mesAtual, mesAtual.AddMonths(1) })
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
                await cobrancas.BloquearAtrasadosAsync(Relogio.Hoje);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Erro na rotina de cobranças");
            }
        } while (await relogio.WaitForNextTickAsync(parar));
    }
}
