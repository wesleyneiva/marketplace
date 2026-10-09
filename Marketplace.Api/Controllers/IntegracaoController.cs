using System.Text;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// "Porta de integração" para outros sistemas (o n8n). Só LEITURA, protegida por chave de API.
// Cada resposta traz os dados e um campo "mensagem" já pronto para o Telegram (texto simples, sem Markdown).
[ApiController]
[Route("api/integracao")]
[AllowAnonymous]
[ChaveApi]
public class IntegracaoController(AppDbContext db, RelatoriosService relatorios, InsightsService insights, LocaisDasLojas locais) : ControllerBase
{
    // GET /api/integracao/resumo?data=2026-10-08 → fechamento do dia (padrão: hoje)
    [HttpGet("resumo")]
    public async Task<object> Resumo(DateOnly? data)
    {
        var dia = data ?? Relogio.Hoje;
        var (inicio, fim) = (Relogio.InicioDoDiaUtc(dia), Relogio.InicioDoDiaUtc(dia.AddDays(1)));
        var doDia = db.Vendas.AsNoTracking().Where(v => v.DataHora >= inicio && v.DataHora < fim);
        var concluidas = doDia.Where(v => v.Status == StatusVenda.Concluida);

        var quantidade = await concluidas.CountAsync();
        var total = await concluidas.SumAsync(v => (decimal?)v.Total) ?? 0;
        var troco = await concluidas.SumAsync(v => (decimal?)v.Troco) ?? 0;
        var custo = await concluidas.SelectMany(v => v.Itens).SumAsync(i => (decimal?)(i.Quantidade * i.CustoUnitario)) ?? 0;
        var canceladas = await doDia.CountAsync(v => v.Status == StatusVenda.Cancelada);

        var pagamentos = await concluidas.SelectMany(v => v.Pagamentos)
            .GroupBy(p => p.Forma).Select(g => new { Forma = g.Key, Valor = g.Sum(p => p.Valor) }).ToListAsync();
        var porForma = Enum.GetValues<FormaPagamento>()
            .Select(f => new
            {
                Forma = NomeForma(f),
                Valor = (pagamentos.FirstOrDefault(p => p.Forma == f)?.Valor ?? 0) - (f == FormaPagamento.Dinheiro ? troco : 0),
            })
            .Where(f => f.Valor > 0).ToList();

        var campeoes = await concluidas.SelectMany(v => v.Itens)
            .GroupBy(i => new { i.Descricao, i.Unidade })
            .Select(g => new { g.Key.Descricao, g.Key.Unidade, Quantidade = g.Sum(i => i.Quantidade), Total = g.Sum(i => i.Total) })
            .OrderByDescending(g => g.Total).Take(5).ToListAsync();

        var caixas = await db.SessoesCaixa.AsNoTracking()
            .Where(s => (s.AbertaEm >= inicio && s.AbertaEm < fim) || s.Status == StatusSessao.Aberta)
            .OrderBy(s => s.NumeroCaixa)
            .Select(s => new { s.NumeroCaixa, Operador = s.Usuario!.NomeCompleto, Status = s.Status.ToString(), s.Diferenca })
            .ToListAsync();

        // Clima do dia na cidade da loja (sem cidade cadastrada: coordenadas impossíveis = sem clima no resumo).
        var local = locais.Local(db.EmpresaAtual);
        var (lat, lon) = local is null ? (999m, 999m) : (local.LatitudeClima, local.LongitudeClima);
        var clima = await db.Clima.AsNoTracking().Where(c => c.Latitude == lat && c.Longitude == lon && c.DataHora >= inicio && c.DataHora < fim)
            .GroupBy(_ => 1)
            .Select(g => new { Minima = g.Min(c => c.Temperatura), Maxima = g.Max(c => c.Temperatura), Chuva = g.Sum(c => c.Chuva) })
            .FirstOrDefaultAsync();

        var estoqueBaixo = await db.Produtos.CountAsync(p => p.Ativo && p.EstoqueAtual <= p.EstoqueMinimo);
        var limite = Relogio.Hoje.AddDays(2);
        var vencendo = await db.Lotes.CountAsync(l => l.QuantidadeAtual > 0 && l.DataValidade <= limite && l.Produto!.Ativo);

        // ----- Mensagem pronta -----
        var m = new StringBuilder();
        m.AppendLine($"🛒 Marketplace — resumo de {dia.ToDateTime(TimeOnly.MinValue):dddd, dd/MM}");
        if (quantidade == 0)
        {
            m.AppendLine("Nenhuma venda registrada no dia.");
        }
        else
        {
            m.AppendLine($"💰 Vendas: {total:C} em {quantidade} venda(s)");
            m.AppendLine($"🧾 Ticket médio: {total / quantidade:C} · Lucro bruto: {total - custo:C}");
            m.AppendLine("💳 " + string.Join(" · ", porForma.Select(f => $"{f.Forma} {f.Valor:C}")));
            m.AppendLine("🏆 Mais vendidos:");
            for (var i = 0; i < campeoes.Count; i++)
                m.AppendLine($"   {i + 1}. {campeoes[i].Descricao} ({Qtd(campeoes[i].Quantidade, campeoes[i].Unidade)}) {campeoes[i].Total:C}");
        }
        if (canceladas > 0) m.AppendLine($"↩️ Canceladas: {canceladas}");
        foreach (var c in caixas)
        {
            var situacao = c.Status == "Aberta" ? "ainda aberto"
                : c.Diferenca is null or 0 ? "fechado ✅ bateu"
                : c.Diferenca > 0 ? $"fechado ⚠️ sobrou {c.Diferenca:C}"
                : $"fechado ❌ faltou {-c.Diferenca:C}";
            m.AppendLine($"🧮 Caixa {c.NumeroCaixa} ({c.Operador}): {situacao}");
        }
        if (clima is not null)
            m.AppendLine($"🌦️ Clima: {clima.Minima:0.#} a {clima.Maxima:0.#} °C" + (clima.Chuva > 0 ? $", {clima.Chuva:0.#} mm de chuva" : ", sem chuva"));
        if (estoqueBaixo > 0 || vencendo > 0)
            m.AppendLine($"⚠️ Atenção: {estoqueBaixo} produto(s) abaixo do mínimo · {vencendo} lote(s) vencendo em até 2 dias");

        return new
        {
            data = dia, vendas = quantidade, faturamento = total,
            ticketMedio = quantidade == 0 ? 0 : Math.Round(total / quantidade, 2),
            lucroBruto = Math.Round(total - custo, 2), canceladas, porForma, maisVendidos = campeoes, caixas, clima,
            estoqueBaixo, lotesVencendo = vencendo,
            mensagem = m.ToString().TrimEnd(),
        };
    }

    // GET /api/integracao/alertas → o que precisa de atenção AGORA. "temAlerta" = false → nada a avisar.
    [HttpGet("alertas")]
    public async Task<object> Alertas()
    {
        var hoje = Relogio.Hoje;
        var inicio = Relogio.InicioDoDiaUtc(hoje);

        var baixo = await db.Produtos.AsNoTracking()
            .Where(p => p.Ativo && p.EstoqueAtual <= p.EstoqueMinimo)
            .OrderBy(p => p.EstoqueMinimo == 0 ? 1 : p.EstoqueAtual / p.EstoqueMinimo) // os mais críticos primeiro
            .Select(p => new { p.Nome, p.EstoqueAtual, p.EstoqueMinimo, p.Unidade })
            .ToListAsync();

        var amanha = hoje.AddDays(1);
        var validade = await db.Lotes.AsNoTracking()
            .Where(l => l.QuantidadeAtual > 0 && l.DataValidade <= amanha && l.Produto!.Ativo)
            .OrderBy(l => l.DataValidade)
            .Select(l => new { Produto = l.Produto!.Nome, l.Produto.Unidade, l.DataValidade, l.QuantidadeAtual })
            .ToListAsync();

        var diferencas = await db.SessoesCaixa.AsNoTracking()
            .Where(s => s.Status == StatusSessao.Fechada && s.FechadaEm >= inicio && s.Diferenca != 0)
            .Select(s => new { s.NumeroCaixa, Operador = s.Usuario!.NomeCompleto, s.Diferenca })
            .ToListAsync();

        // Contas a pagar vencidas, de hoje e de amanhã (dá tempo de separar o dinheiro).
        var contas = await db.ContasPagar.AsNoTracking()
            .Where(c => c.PagaEm == null && c.Vencimento <= amanha)
            .OrderBy(c => c.Vencimento)
            .Select(c => new { c.Descricao, c.Vencimento, c.Valor })
            .ToListAsync();

        var total = baixo.Count + validade.Count + diferencas.Count + contas.Count;
        string? mensagem = null;
        if (total > 0)
        {
            var m = new StringBuilder($"🚨 Marketplace — {total} alerta(s)\n");
            if (baixo.Count > 0)
            {
                m.AppendLine($"\n📦 Estoque baixo ({baixo.Count}):");
                foreach (var p in baixo.Take(10))
                    m.AppendLine($"• {p.Nome}: {Qtd(p.EstoqueAtual, p.Unidade)} (mín. {Qtd(p.EstoqueMinimo, p.Unidade)})");
                if (baixo.Count > 10) m.AppendLine($"… e mais {baixo.Count - 10}");
            }
            if (validade.Count > 0)
            {
                m.AppendLine($"\n⏰ Validade ({validade.Count}):");
                foreach (var l in validade.Take(10))
                {
                    var dias = l.DataValidade.DayNumber - hoje.DayNumber;
                    var quando = dias < 0 ? $"🔴 venceu em {l.DataValidade:dd/MM}" : dias == 0 ? "🟠 vence hoje" : "🟡 vence amanhã";
                    m.AppendLine($"• {l.Produto}: {Qtd(l.QuantidadeAtual, l.Unidade)} {quando}");
                }
                if (validade.Count > 10) m.AppendLine($"… e mais {validade.Count - 10}");
            }
            if (diferencas.Count > 0)
            {
                m.AppendLine("\n💵 Diferença no fechamento de caixa:");
                foreach (var d in diferencas)
                    m.AppendLine($"• Caixa {d.NumeroCaixa} ({d.Operador}): " + (d.Diferenca > 0 ? $"sobrou {d.Diferenca:C}" : $"faltou {-d.Diferenca:C}"));
            }
            if (contas.Count > 0)
            {
                m.AppendLine($"\n💸 Contas a pagar ({contas.Count} · {contas.Sum(c => c.Valor):C}):");
                foreach (var c in contas.Take(10))
                {
                    var dias = c.Vencimento.DayNumber - hoje.DayNumber;
                    var quando = dias < 0 ? $"🔴 venceu em {c.Vencimento:dd/MM}" : dias == 0 ? "🟠 vence hoje" : "🟡 vence amanhã";
                    m.AppendLine($"• {c.Descricao}: {c.Valor:C} {quando}");
                }
                if (contas.Count > 10) m.AppendLine($"… e mais {contas.Count - 10}");
            }
            mensagem = m.ToString().TrimEnd();
        }

        return new { temAlerta = total > 0, total, estoqueBaixo = baixo, validade, diferencasCaixa = diferencas, contasPagar = contas, mensagem };
    }

    // GET /api/integracao/estoque?busca=arroz → para o comando /estoque do bot
    [HttpGet("estoque")]
    public async Task<object> Estoque(string? busca)
    {
        var termo = (busca ?? "").Trim();
        if (termo.Length < 2)
            return new { encontrados = 0, mensagem = "Use assim: /estoque arroz  (ou parte do nome do produto)" };

        var produtos = await db.Produtos.AsNoTracking()
            .Where(p => p.Ativo && (EF.Functions.ILike(EF.Functions.Unaccent(p.Nome), EF.Functions.Unaccent($"%{termo}%")) || p.CodigoBarras == termo))
            .OrderBy(p => p.Nome).Take(6)
            .Select(p => new
            {
                p.Id, p.Nome, p.Unidade, p.EstoqueAtual, p.EstoqueMinimo, p.PrecoVenda,
                ProximoLote = p.Lotes.Where(l => l.QuantidadeAtual > 0).OrderBy(l => l.DataValidade)
                    .Select(l => new { l.DataValidade, l.QuantidadeAtual }).FirstOrDefault(),
            })
            .ToListAsync();

        if (produtos.Count == 0)
            return new { encontrados = 0, mensagem = $"Não encontrei nenhum produto com \"{termo}\"." };

        var m = new StringBuilder($"📦 Estoque — \"{termo}\"\n");
        foreach (var p in produtos.Take(5))
        {
            var alerta = p.EstoqueAtual <= p.EstoqueMinimo ? " ⚠️ abaixo do mínimo" : "";
            m.AppendLine($"\n{p.Nome}\n   {Qtd(p.EstoqueAtual, p.Unidade)} (mín. {Qtd(p.EstoqueMinimo, p.Unidade)}){alerta} · {p.PrecoVenda:C}/{p.Unidade}");
            if (p.ProximoLote is not null)
                m.AppendLine($"   Próxima validade: {p.ProximoLote.DataValidade:dd/MM} ({Qtd(p.ProximoLote.QuantidadeAtual, p.Unidade)})");
        }
        if (produtos.Count > 5) m.AppendLine("\n… há mais produtos: seja mais específico.");

        return new { encontrados = produtos.Count, produtos, mensagem = m.ToString().TrimEnd() };
    }

    // GET /api/integracao/semana → fechamento da SEMANA (segunda até hoje) para o n8n mandar no sábado:
    //   "mensagem": os números prontos para o Telegram
    //   "prompt":   o pedido para a IA (o n8n passa ao Gemini e junta a resposta à mensagem)
    [HttpGet("semana")]
    public async Task<object> Semana(CancellationToken ct)
    {
        var hoje = Relogio.Hoje;
        if (hoje.DayOfWeek == DayOfWeek.Sunday) hoje = hoje.AddDays(-1); // domingo (fechado): fala da semana que passou
        var segunda = hoje.AddDays(-(((int)hoje.DayOfWeek + 6) % 7));
        var periodo = RelatoriosService.Ler(segunda, hoje);

        var vendas = await relatorios.Vendas(periodo.De, periodo.Ate);
        var produtos = await relatorios.Produtos(periodo.De, periodo.Ate);
        var i = vendas.Indicadores;
        var dias = vendas.PorDia.Where(d => d.Vendas > 0).ToList();
        var melhor = dias.OrderByDescending(d => d.Faturamento).FirstOrDefault();
        var pior = dias.OrderBy(d => d.Faturamento).FirstOrDefault();
        string Variacao(decimal? v) => v is null ? "" : $" ({(v >= 0 ? "▲" : "▼")} {Math.Abs(v.Value):0.#}% vs semana anterior)";

        var m = new StringBuilder();
        m.AppendLine($"📅 Marketplace — semana de {periodo.De:dd/MM} a {periodo.Ate:dd/MM}");
        m.AppendLine($"💰 Faturamento: {i.Faturamento:C}{Variacao(i.VariacaoFaturamento)}");
        m.AppendLine($"🧾 {i.Vendas} vendas · ticket médio {i.TicketMedio:C}{Variacao(i.VariacaoTicket)}");
        m.AppendLine($"📈 Lucro bruto: {i.LucroBruto:C} (margem {i.MargemPercentual:0.#}%)");
        if (melhor is not null && pior is not null)
            m.AppendLine($"🏅 Melhor dia: {melhor.Data:dd/MM} ({melhor.Faturamento:C0}) · mais fraco: {pior.Data:dd/MM} ({pior.Faturamento:C0})");
        m.AppendLine("🏆 Campeões: " + string.Join(", ", produtos.Produtos.Take(3).Select(p => $"{p.Nome} ({p.Faturamento:C0})")));

        var (_, prompt) = await insights.MontarAsync(periodo,
            "FOCO: é o fechamento da semana. Comente a semana em 1 item e use os outros para planejar a PRÓXIMA semana " +
            "(compras, estoque, equipe por dia) com base na previsão do tempo.", ct);

        return new { periodo, mensagem = m.ToString().TrimEnd(), prompt };
    }

    private static string Qtd(decimal valor, string unidade) =>
        unidade is Unidades.Quilo or Unidades.Litro ? $"{valor:0.000} {unidade}" : $"{valor:0} {unidade}";

    private static string NomeForma(FormaPagamento f) => f switch
    {
        FormaPagamento.Debito => "Débito",
        FormaPagamento.Credito => "Crédito",
        _ => f.ToString(),
    };
}
