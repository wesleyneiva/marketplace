using System.Collections.Concurrent;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// COMPRAS AUTOMÁTICAS: todo dia, quando a loja abre, monta os pedidos de compra em RASCUNHO pela sugestão de compra
// (um por fornecedor). Nada é enviado: a pessoa confere, ajusta as quantidades e envia. O rascunho automático de um dia
// que não foi enviado é trocado pelo do dia seguinte (com as contas refeitas). Vale para as empresas com
// Empresa.ComprasAutomaticas, menos a demonstração (lá o próprio robô compra).
public class ComprasAutomaticas(IServiceScopeFactory escopos, LocaisDasLojas locais, ILogger<ComprasAutomaticas> log) : BackgroundService
{
    private readonly ConcurrentDictionary<int, DateOnly> _feitoEm = new();

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        await Task.Delay(TimeSpan.FromSeconds(90), parar);
        using var relogio = new PeriodicTimer(TimeSpan.FromMinutes(10));
        do
        {
            List<int> empresas;
            using (var scope = escopos.CreateScope())
            {
                empresas = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Empresas.AsNoTracking()
                    .Where(e => e.Ativa && !e.Plataforma && e.ComprasAutomaticas && (e.Modulos == null || e.Modulos.Contains(Modulos.Compras)) && e.VendasAutomaticas != ModosVendasAutomaticas.Completo)
                    .Select(e => e.Id).ToListAsync(parar);
            }
            foreach (var id in empresas)
            {
                try { await RodarAsync(id, parar); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Erro nas compras automáticas da empresa {Empresa}", id);
                }
            }
        } while (await relogio.WaitForNextTickAsync(parar));
    }

    private async Task RodarAsync(int empresaId, CancellationToken ct)
    {
        locais.Aplicar(empresaId);
        var hoje = Relogio.Hoje;
        if (_feitoEm.TryGetValue(empresaId, out var dia) && dia == hoje) return;
        if (Expediente.DoDia(hoje) is not { } expediente || TimeOnly.FromDateTime(Relogio.Agora.DateTime) < expediente.Abre) return;

        using var scope = escopos.CriarEscopo(empresaId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Já montou hoje (ex.: a API reiniciou no meio do dia)?
        if (await db.PedidosCompra.AnyAsync(p => p.SugestaoAutomatica && p.CriadoEm >= Relogio.InicioDoDiaUtc(hoje), ct))
        {
            _feitoEm[empresaId] = hoje;
            return;
        }
        var novos = await GerarAsync(scope.ServiceProvider, hoje, ct);
        _feitoEm[empresaId] = hoje;
        log.LogInformation("Compras automáticas da empresa {Empresa}: {Pedidos} rascunho(s).", empresaId, novos);
    }

    // Troca os rascunhos automáticos antigos (não enviados) pelos de hoje. Também usado pelo botão "Refazer agora".
    public static async Task<int> GerarAsync(IServiceProvider servicos, DateOnly hoje, CancellationToken ct = default)
    {
        var db = servicos.GetRequiredService<AppDbContext>();
        var compras = servicos.GetRequiredService<ComprasService>();

        var antigos = await db.PedidosCompra.Where(p => p.SugestaoAutomatica && p.Status == StatusPedido.Rascunho).ToListAsync(ct);
        antigos.ForEach(p => p.Status = StatusPedido.Cancelado);
        await db.SaveChangesAsync(ct); // cancelados antes: senão a sugestão os contaria como "a caminho"

        var responsavel = await ResponsavelAsync(servicos);
        if (responsavel is null) return 0;

        var novos = 0;
        foreach (var s in await compras.SugestaoAsync(ct))
        {
            var itens = s.Itens.Select(i => new ItemPedidoRequest(i.ProdutoId, i.Sugerido, null)).ToList();
            if (itens.Count == 0) continue;
            var pedido = await compras.CriarAsync(new NovoPedidoRequest(s.FornecedorId, itens,
                $"Sugestão automática de {hoje:dd/MM}: confira as quantidades e envie ao fornecedor.", Enviar: false), responsavel);
            pedido.SugestaoAutomatica = true;
            await db.SaveChangesAsync(ct);
            novos++;
        }
        return novos;
    }

    // Quem aparece como "criado por": o primeiro administrador ativo da loja.
    private static async Task<string?> ResponsavelAsync(IServiceProvider servicos)
    {
        var db = servicos.GetRequiredService<AppDbContext>();
        var usuarios = servicos.GetRequiredService<UserManager<Usuario>>();
        foreach (var u in await db.Users.Where(u => u.Ativo && !u.SomenteLeitura).OrderBy(u => u.CriadoEm).ToListAsync())
            if (await usuarios.IsInRoleAsync(u, Perfis.Administrador)) return u.Id;
        return null;
    }
}
