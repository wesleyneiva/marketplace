using System.Text.Encodings.Web;
using System.Text.Json;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services.Simulador;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

public record ResultadoIa(bool Ok, string? Texto, int Status, string? Mensagem);

// Monta o "pedido" para a IA: um resumo COMPACTO do período + instruções. Quem fala com o Gemini é o n8n.
// Usado pelo botão da tela de Relatórios e pelo relatório semanal (que o n8n busca às sextas/sábados).
public class InsightsService(
    AppDbContext db, RelatoriosService relatorios, ComprasService compras, ClimaService clima, LocaisDasLojas locais,
    IHttpClientFactory http, IConfiguration config, ILogger<InsightsService> log)
{
    private static readonly string[] NomesDias = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<(object Dados, string Prompt)> MontarAsync(Periodo periodo, string foco, CancellationToken ct)
    {
        var vendas = await relatorios.Vendas(periodo.De, periodo.Ate);
        var produtos = await relatorios.Produtos(periodo.De, periodo.Ate);
        var efeitoClima = await relatorios.Clima(periodo.De, periodo.Ate);

        var local = locais.Local(db.EmpresaAtual);
        var empresa = await db.Empresas.AsNoTracking().Where(e => e.Id == db.EmpresaAtual).Select(e => new { e.Nome, e.Demonstracao, e.Ramo }).FirstAsync(ct);
        List<PrevisaoDia> previsao = [];
        if (local is not null)
        {
            try { previsao = await clima.PrevisaoAsync(local, 7, ct); }
            catch (Exception e) { log.LogWarning("Previsão indisponível: {Erro}", e.Message); }
        }

        var hoje = Relogio.Hoje;
        var estoqueBaixo = await db.Produtos.Where(p => p.Ativo && !p.Servico && p.EstoqueAtual <= p.EstoqueMinimo).Select(p => p.Nome).ToListAsync(ct);
        var vencendo = await db.Lotes.Where(l => l.QuantidadeAtual > 0 && l.DataValidade <= hoje.AddDays(2) && l.Produto!.Ativo)
            .Select(l => l.Produto!.Nome).Distinct().ToListAsync(ct);

        // Compras: o que já está a caminho e o que corre risco de faltar ANTES da próxima entrega.
        var aCaminho = await db.PedidosCompra.Where(p => p.Status == StatusPedido.Enviado)
            .Select(p => $"{p.Fornecedor!.Nome}: chega {p.PrevisaoEntrega}").ToListAsync(ct);
        var riscoDeFaltar = (await compras.SugestaoAsync(ct))
            .SelectMany(s => s.Itens.Where(i => i.DiasDeEstoque is not null && i.DiasDeEstoque < s.PrazoEntregaDias)
                .Select(i => $"{i.Produto} (dura ~{i.DiasDeEstoque:0.#} dia(s), entrega em {s.PrazoEntregaDias})"))
            .ToList();


        var top = vendas.MapaDeCalor.OrderByDescending(c => c.MediaVendas).Take(3)
            .Select(c => $"{NomesDias[c.DiaSemana]} {c.Hora}h ({c.MediaVendas} clientes)").ToList();

        // Dicionário com nomes EM PORTUGUÊS: a IA lê como texto e não copia nomes técnicos ("variacaoPct") na resposta.
        var agora = Relogio.Agora;
        var hojeParcial = periodo.Ate == hoje && Expediente.AbertoNaHora(hoje, agora.Hour);
        var negocio = Ramos.Negocio(empresa.Ramo);
        var dados = new Dictionary<string, object?>
        {
            ["Negócio"] = empresa.Demonstracao
                ? $"{Maiuscula(negocio)} de demonstração em {local?.Nome ?? "Porto Alegre/RS"}. Horário: {Expediente.Descricao()}."
                : $"{Maiuscula(negocio)} \"{empresa.Nome}\"" + (local is null ? "" : $" em {local.Nome}") + $". Horário: {Expediente.Descricao()}.",
            ["Período analisado"] = $"{periodo.De:dd/MM/yyyy} a {periodo.Ate:dd/MM/yyyy} ({periodo.Dias} dias)",
            ["Atenção"] = hojeParcial
                ? $"O dia de hoje ({hoje:dd/MM}) ainda está em andamento (agora são {agora:HH:mm}): os números de hoje são PARCIAIS, não compare hoje com dias inteiros."
                : null,
            ["Números do período"] = new[]
            {
                $"Faturamento: {vendas.Indicadores.Faturamento:C}" + Variacao(vendas.Indicadores.VariacaoFaturamento, "período anterior"),
                $"Vendas: {vendas.Indicadores.Vendas}; ticket médio {vendas.Indicadores.TicketMedio:C}" + Variacao(vendas.Indicadores.VariacaoTicket, "período anterior"),
                $"Margem bruta: {vendas.Indicadores.MargemPercentual:0.#}%; faturamento médio por dia aberto: {vendas.Indicadores.FaturamentoMedioDia:C}",
            },
            ["Média por dia da semana"] = vendas.PorDiaSemana.Select(d => $"{d.Nome}: {d.Faturamento:C0} ({d.Vendas} vendas)"),
            ["Horários de pico"] = top,
            ["Formas de pagamento"] = vendas.PorForma.Select(f => $"{f.Forma}: {f.Valor:C0}"),
            ["Curva ABC"] = produtos.Classes.Select(c => $"Classe {c.Classe}: {c.Produtos} produtos, {c.Participacao}% do faturamento"),
            ["10 produtos que mais faturam"] = produtos.Produtos.Take(10).Select(p => $"{p.Nome} ({p.Classe}): {p.Faturamento:C0}, margem {p.MargemPercentual}%"),
            ["Categorias"] = produtos.Categorias.Select(c => $"{c.Categoria}: {c.Participacao}% do faturamento, margem {c.MargemPercentual}%"),
            // Já CALCULADO e testado (a IA erra menos quando não precisa fazer a conta nem adivinhar).
            ["Efeito da chuva no movimento"] = efeitoClima.AvisoChuva,
            ["Clientes por hora, por temperatura"] = efeitoClima.PorTemperatura.Count == 0
                ? "dados insuficientes"
                : efeitoClima.PorTemperatura.Select(f => $"{f.Faixa}: {f.ClientesPorHora}/h ({f.Horas} horas)"),
            ["Produtos que vendem mais no calor"] = efeitoClima.SobemNoCalor.Select(p => $"{p.Nome}: {p.Fator}x no calor"),
            ["Produtos que vendem mais no frio"] = efeitoClima.SobemNoFrio.Select(p => $"{p.Nome}: {p.Fator}x no frio"),
            ["Produtos que vendem mais na chuva"] = efeitoClima.SobemNaChuva.Select(p => $"{p.Nome}: {p.Fator}x na chuva"),
            ["Previsão do tempo"] = previsao.Select(p =>
                $"{p.Data:dd/MM} {NomesDias[(int)p.Data.DayOfWeek]}{(Expediente.AbertoNoDia(p.Data) ? "" : " (FECHADO)")}: " +
                $"{p.Minima}–{p.Maxima} °C, {p.Tempo}, chuva {p.ChanceChuva}% ({p.Chuva} mm)"),
            ["Produtos abaixo do estoque mínimo"] = estoqueBaixo,
            ["Lotes vencendo em até 2 dias"] = vencendo,
            ["Pedidos de compra a caminho"] = aCaminho,
            ["Produtos que podem faltar antes da próxima entrega"] = riscoDeFaltar,
        };

        var prompt = $"""
            Você é um consultor de varejo experiente e direto. Analise os dados {(empresa.Ramo == Ramos.Esporte ? "da loja" : "do mercado")} abaixo e escreva de 4 a 6
            recomendações práticas para o gerente, em português do Brasil. {foco}
            {DoRamo(empresa.Ramo)}

            Regras:
            - Formato: uma lista; cada item começa com um título curto (sem emojis), seguido de "—" e no máximo 2 frases.
            - Cite os números dos dados para justificar, cada número com o SEU contexto (ex.: um fator "1,6x no frio"
              não pode ser usado para falar de chuva).
            - NÃO invente números, produtos ou fatos que não estejam nos dados.
            - CLIMA: fale do efeito da chuva ou da temperatura no movimento SOMENTE como está em "Efeito da chuva no
              movimento" e "Clientes por hora, por temperatura". Se lá disser "sem diferença confiável", "ainda juntando
              dados" ou "dados insuficientes", NÃO suponha que a chuva ou o calor aumentam ou reduzem o movimento deste
              negócio. Produtos sensíveis ao clima: só os listados (já passaram em teste estatístico).
            - Use a PREVISÃO DO TEMPO e os dias FECHADOS para planejar estoque e compras dos próximos dias.
            - Se houver "Produtos que podem faltar antes da próxima entrega", avise com prioridade (pedir JÁ).
            - "Lotes vencendo em até 2 dias" são lotes perto de vencer: sugira promoção/destaque para vender antes.
              Isso NÃO significa comprar menos — o mesmo produto pode ter um lote vencendo e precisar de compra.
            - Correlação não é causa: padrões de clima podem ter outra explicação (as horas frias são de manhã,
              quando o café da manhã vende mais). Use "parece", "os dados sugerem" quando não for certeza.
            - Antes de responder, confira: nenhuma recomendação pode contradizer outra.
            - Respeite o aviso de "Atenção", se houver (dados parciais do dia de hoje).
            - Escreva os nomes das informações de forma natural, nunca como nomes técnicos.
            - Não use Markdown (nada de **, # ou tabelas). Texto simples.

            Dados (JSON):
            {JsonSerializer.Serialize(dados, Json)}
            """;
        return (dados, prompt);
    }

    // O que olhar em cada ramo. Só PONTOS DE ATENÇÃO: a IA continua proibida de afirmar o que não está nos dados.
    private static string DoRamo(string ramo) => ramo == Ramos.Esporte
        ? """
          É uma loja de material esportivo (ex.: tênis: raquetes, calçados, roupas, bolas, cordas, acessórios). Pontos de
          atenção típicos do ramo, para usar SÓ quando os dados mostrarem: itens de reposição frequente (bolas, cordas,
          overgrips) não podem faltar; produtos caros parados prendem dinheiro; venda de maior valor pede atenção ao
          parcelamento e ao atendimento. Não fale de validade nem de perecíveis, a não ser que apareçam nos dados.
          """
        : "";

    private static string Maiuscula(string t) => t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];

    private static string Variacao(decimal? v, string contra) =>
        v is null ? "" : $" ({(v >= 0 ? "alta" : "queda")} de {Math.Abs(v.Value):0.#}% contra o {contra})";

    // Envia ao fluxo do n8n (Webhook → Gemini → Respond to Webhook) e devolve o texto.
    public async Task<ResultadoIa> PedirAoN8nAsync(object dados, string prompt, CancellationToken ct)
    {
        var url = config["Integracao:N8nInsightsUrl"] ?? "http://127.0.0.1:5678/webhook/marketplace-insights";
        var cliente = http.CreateClient();
        cliente.Timeout = TimeSpan.FromMinutes(4); // em segundo plano (InsightsTarefas): pode esperar o Google sobrecarregado
        // StringContent (e não JsonContent): manda o JSON inteiro com Content-Length, o formato mais compatível.
        using var pedido = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { prompt, dados }, Json), System.Text.Encoding.UTF8, "application/json"),
        };
        pedido.Headers.Add("X-Api-Key", config["Integracao:ChaveApi"] ?? "");

        try
        {
            using var resposta = await cliente.SendAsync(pedido, ct);
            if (resposta.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(false, null, 503, "O fluxo de insights ainda não está publicado no n8n.");
            if (!resposta.IsSuccessStatusCode)
            {
                log.LogWarning("Insights: o n8n respondeu {Status}", (int)resposta.StatusCode);
                // O fluxo falha quando o Google (Gemini) responde 503 = sobrecarregado (mesmo depois das novas tentativas).
                return new(false, null, 502, "A IA do Google está sobrecarregada agora e não respondeu. Tente de novo em alguns minutos.");
            }

            var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
            var texto = corpo.TryGetProperty("insights", out var t) ? t.GetString() : null;
            return string.IsNullOrWhiteSpace(texto)
                ? new(false, null, 502, "O n8n respondeu, mas sem o campo \"insights\".")
                : new(true, texto.Trim(), 200, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, 504, "A IA demorou demais para responder (mais de 4 minutos). Tente de novo em alguns minutos.");
        }
        catch (HttpRequestException e)
        {
            log.LogWarning("Falha ao chamar o n8n: {Erro}", e.Message);
            return new(false, null, 503, "Não foi possível falar com o n8n.");
        }
    }
}
