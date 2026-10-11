using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Situação de um cliente na caderneta.
//   Saldo     = compras − pagamentos − estornos − baixas
//   Vencido   = parte do saldo cujas compras já venceram (os pagamentos abatem as compras MAIS ANTIGAS primeiro)
//   DiasAtraso = há quantos dias venceu a compra mais antiga ainda não paga
public record SituacaoFiado(decimal Saldo, decimal Vencido, int DiasAtraso, DateOnly? ProximoVencimento, decimal ValorProximo,
    DateTimeOffset? UltimaCompra, DateTimeOffset? UltimoPagamento);

// Regras do fiado (usadas pelo PDV, pela tela da caderneta, pela DRE e pelo fluxo de caixa).
public class FiadoService(AppDbContext db)
{
    public const int DiasDeTolerancia = 10; // atrasado há mais que isso: só gerente vende fiado

    // A compra vence no "dia de pagamento" do cliente, no mês seguinte ao da compra (como a caderneta de verdade).
    public static DateOnly Vencimento(DateOnly compra, int diaPagamento)
    {
        var mes = new DateOnly(compra.Year, compra.Month, 1).AddMonths(1);
        return new DateOnly(mes.Year, mes.Month, Math.Clamp(diaPagamento, 1, 28));
    }

    public async Task<Dictionary<int, SituacaoFiado>> SituacoesAsync(IReadOnlyCollection<int>? clientes = null)
    {
        var q = db.LancamentosFiado.AsNoTracking();
        if (clientes is not null) q = q.Where(l => clientes.Contains(l.ClienteId));
        var lancamentos = await q.Select(l => new { l.ClienteId, l.Tipo, l.Valor, l.Vencimento, l.DataHora }).ToListAsync();
        var hoje = Relogio.Hoje;

        return lancamentos.GroupBy(l => l.ClienteId).ToDictionary(g => g.Key, g =>
        {
            var compras = g.Where(l => l.Tipo == TipoLancamentoFiado.Compra).OrderBy(l => l.Vencimento).ThenBy(l => l.DataHora).ToList();
            var abatido = g.Where(l => l.Tipo != TipoLancamentoFiado.Compra).Sum(l => l.Valor);
            var saldo = compras.Sum(c => c.Valor) - abatido;

            // FIFO: o que foi pago quita as compras mais antigas; sobram as "em aberto".
            decimal vencido = 0, valorProximo = 0;
            DateOnly? maisAntigaAberta = null, proximo = null;
            foreach (var c in compras)
            {
                var abate = Math.Min(abatido, c.Valor);
                abatido -= abate;
                var aberto = c.Valor - abate;
                if (aberto <= 0) continue;
                var venc = c.Vencimento ?? hoje;
                if (venc < hoje) { vencido += aberto; maisAntigaAberta ??= venc; }
                else if (proximo is null || venc == proximo) { proximo = venc; valorProximo += aberto; }
            }
            return new SituacaoFiado(saldo, vencido, maisAntigaAberta is { } d ? hoje.DayNumber - d.DayNumber : 0, proximo, valorProximo,
                g.Where(l => l.Tipo == TipoLancamentoFiado.Compra).Max(l => (DateTimeOffset?)l.DataHora),
                g.Where(l => l.Tipo == TipoLancamentoFiado.Pagamento).Max(l => (DateTimeOffset?)l.DataHora));
        });
    }

    // O que ainda está em aberto, compra a compra (pagamentos abatem as mais antigas): para o fluxo de caixa previsto.
    public async Task<List<(int ClienteId, string Cliente, DateOnly Vencimento, decimal Aberto)>> EmAbertoAsync()
    {
        var lancamentos = await db.LancamentosFiado.AsNoTracking()
            .Select(l => new { l.ClienteId, Cliente = l.Cliente!.Nome, l.Tipo, l.Valor, l.Vencimento, l.DataHora }).ToListAsync();
        var lista = new List<(int, string, DateOnly, decimal)>();
        foreach (var g in lancamentos.GroupBy(l => l.ClienteId))
        {
            var abatido = g.Where(l => l.Tipo != TipoLancamentoFiado.Compra).Sum(l => l.Valor);
            foreach (var c in g.Where(l => l.Tipo == TipoLancamentoFiado.Compra).OrderBy(l => l.Vencimento).ThenBy(l => l.DataHora))
            {
                var abate = Math.Min(abatido, c.Valor);
                abatido -= abate;
                if (c.Valor - abate > 0) lista.Add((g.Key, c.Cliente, c.Vencimento ?? Relogio.Hoje, c.Valor - abate));
            }
        }
        return lista;
    }

    public async Task<SituacaoFiado> SituacaoAsync(int clienteId) =>
        (await SituacoesAsync([clienteId])).GetValueOrDefault(clienteId) ?? new SituacaoFiado(0, 0, 0, null, 0, null, null);

    // Por que este cliente NÃO pode comprar "valor" no fiado sem um gerente (nulo = pode).
    public static string? MotivoParaGerente(Cliente c, SituacaoFiado s, decimal valor)
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        if (c.Bloqueado) return $"{c.Nome} está bloqueado(a) para fiado.";
        if (c.LimiteCredito <= 0) return $"{c.Nome} não tem limite de fiado.";
        if (s.DiasAtraso > DiasDeTolerancia) return $"{c.Nome} está com o fiado atrasado há {s.DiasAtraso} dias ({s.Vencido.ToString("C", pt)}).";
        if (s.Saldo + valor > c.LimiteCredito)
            return $"Passa do limite de {c.Nome}: deve {s.Saldo.ToString("C", pt)}, limite {c.LimiteCredito.ToString("C", pt)}, disponível {Math.Max(0, c.LimiteCredito - s.Saldo).ToString("C", pt)}.";
        return null;
    }

    // Venda com fiado (chamado pelo VendaService, dentro da transação da venda).
    public async Task RegistrarCompraAsync(Venda venda, int? clienteId, decimal valorFiado, bool podeLiberar, string usuarioId)
    {
        if (clienteId is not int id) throw new EstoqueException("Fiado: escolha o cliente.");
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id) ?? throw new EstoqueException("Cliente não encontrado.");
        if (!cliente.Ativo) throw new EstoqueException($"{cliente.Nome} está desativado(a).");
        var motivo = MotivoParaGerente(cliente, await SituacaoAsync(id), valorFiado);
        if (motivo is not null && !podeLiberar) throw new EstoqueException(motivo + " Só um gerente pode liberar.");

        db.LancamentosFiado.Add(new LancamentoFiado
        {
            ClienteId = id, Venda = venda, Tipo = TipoLancamentoFiado.Compra, Valor = valorFiado, UsuarioId = usuarioId,
            Vencimento = Vencimento(Relogio.DiaLocal(venda.DataHora), cliente.DiaPagamento),
            Observacao = motivo is null ? null : "Liberado por gerente: " + Cortar(motivo, 150),
        });
    }

    // Venda cancelada: o fiado dela sai da caderneta.
    public async Task EstornarCompraAsync(Venda venda, string usuarioId, string motivo)
    {
        var compra = await db.LancamentosFiado.AsNoTracking()
            .FirstOrDefaultAsync(l => l.VendaId == venda.Id && l.Tipo == TipoLancamentoFiado.Compra);
        if (compra is null) return;
        db.LancamentosFiado.Add(new LancamentoFiado
        {
            ClienteId = compra.ClienteId, VendaId = venda.Id, Tipo = TipoLancamentoFiado.Estorno, Valor = compra.Valor,
            UsuarioId = usuarioId, Observacao = Cortar("Venda cancelada: " + motivo, 200),
        });
    }

    private static string Cortar(string t, int n) => t.Length > n ? t[..n] : t;
}
