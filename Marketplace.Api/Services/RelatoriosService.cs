using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services.Simulador;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Os cálculos dos relatórios (usados pela tela de Relatórios, pelos insights da IA e pelo relatório semanal).
// SQL "puro" (db.Database.SqlQuery): para agrupar por dia/hora no fuso de Brasília, preencher dias sem venda e
// cruzar com o clima, o SQL é mais claro que o LINQ. Os valores entre { } viram PARÂMETROS (nunca texto colado no
// SQL) — isso evita "SQL injection". Considera só vendas CONCLUÍDAS, de todas as origens.
// ATENÇÃO (multi-tenant): o filtro automático por empresa do EF NÃO vale para SQL puro. Por isso toda consulta
// aqui tem  "EmpresaId" = {db.EmpresaAtual}  escrito à mão. SQL novo → lembrar disso!
public class RelatoriosService(AppDbContext db, LocaisDasLojas locais)
{
    private static readonly string[] NomesDias = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];
    private const decimal Quente = 24m, Frio = 15m; // °C: "hora quente" ≥ 24, "hora fria" < 15
    private const double ZMinimo = 3.0;             // confiança exigida para mostrar um padrão de clima (~99,7%)

    // Vendas: números do período, por dia, por dia da semana, mapa de calor e formas de pagamento.
    public async Task<RelatorioVendas> Vendas(DateOnly? de, DateOnly? ate)
    {
        var periodo = Ler(de, ate);
        var dias = await PorDiaAsync(periodo.De, periodo.Ate);

        // Mesmo número de dias, logo antes, para comparar ("+12% contra os 30 dias anteriores").
        var anterior = await PorDiaAsync(periodo.De.AddDays(-periodo.Dias), periodo.De.AddDays(-1));

        var (ini, fim) = Utc(periodo);
        var horas = await db.Database.SqlQuery<LinhaHora>($"""
            SELECT extract(dow  FROM v."DataHora" AT TIME ZONE {Relogio.FusoId})::int AS "DiaSemana",
                   extract(hour FROM v."DataHora" AT TIME ZONE {Relogio.FusoId})::int AS "Hora",
                   count(*)::int AS "Vendas"
            FROM "Vendas" v
            WHERE v."EmpresaId" = {db.EmpresaAtual} AND v."Status" = 'Concluida' AND v."DataHora" >= {ini} AND v."DataHora" < {fim}
            GROUP BY 1, 2
            """).ToListAsync();

        // Pagamentos: no dinheiro, desconta o troco (o que ficou de fato no caixa).
        var formas = await db.Database.SqlQuery<LinhaForma>($"""
            SELECT p."Forma" AS "Forma",
                   sum(p."Valor") - CASE WHEN p."Forma" = 'Dinheiro'
                       THEN (SELECT coalesce(sum(v2."Troco"), 0) FROM "Vendas" v2
                             WHERE v2."EmpresaId" = {db.EmpresaAtual} AND v2."Status" = 'Concluida' AND v2."DataHora" >= {ini} AND v2."DataHora" < {fim})
                       ELSE 0 END AS "Valor"
            FROM "PagamentosVenda" p JOIN "Vendas" v ON v."Id" = p."VendaId"
            WHERE v."EmpresaId" = {db.EmpresaAtual} AND v."Status" = 'Concluida' AND v."DataHora" >= {ini} AND v."DataHora" < {fim}
            GROUP BY p."Forma" ORDER BY 2 DESC
            """).ToListAsync();

        // Médias só com os dias em que o mercado ABRIU (domingo e feriado fechado não puxam a média para baixo).
        var abertos = dias.Where(d => ComportamentoCliente.AbertoNoDia(d.Data)).ToList();

        // Quantas vezes cada dia da semana aparece no período (para tirar a MÉDIA, e não a soma).
        var ocorrencias = abertos.GroupBy(d => (int)d.Data.DayOfWeek).ToDictionary(g => g.Key, g => g.Count());

        var porDiaSemana = Enumerable.Range(0, 7)
            .Where(ocorrencias.ContainsKey)
            .Select(d =>
            {
                var doDia = abertos.Where(x => (int)x.Data.DayOfWeek == d).ToList();
                return new MediaDiaSemana(d, NomesDias[d],
                    Math.Round((decimal)doDia.Average(x => x.Vendas), 1), Math.Round(doDia.Average(x => x.Faturamento), 2));
            })
            .ToList();

        var mapa = horas
            .Select(h => new CelulaCalor(h.DiaSemana, h.Hora, Math.Round((decimal)h.Vendas / ocorrencias.GetValueOrDefault(h.DiaSemana, 1), 1)))
            .OrderBy(c => c.DiaSemana).ThenBy(c => c.Hora)
            .ToList();

        return new RelatorioVendas(
            periodo, CalcularIndicadores(dias, anterior, abertos.Count),
            dias.Select(d => new VendaDia(d.Data, d.Vendas, d.Faturamento, Math.Round(d.Faturamento - d.Custo, 2), d.TempMax, d.TempMin, d.Chuva)).ToList(),
            porDiaSemana, mapa, formas);
    }

    // Produtos: curva ABC + categorias.
    public async Task<RelatorioProdutos> Produtos(DateOnly? de, DateOnly? ate)
    {
        var periodo = Ler(de, ate);
        var (ini, fim) = Utc(periodo);

        var linhas = await db.Database.SqlQuery<LinhaProduto>($"""
            SELECT p."Id" AS "ProdutoId", p."Nome" AS "Nome", c."Nome" AS "Categoria", p."Unidade" AS "Unidade",
                   sum(i."Quantidade") AS "Quantidade", count(DISTINCT v."Id")::int AS "Vendas",
                   sum(i."Total") AS "Faturamento", sum(i."Quantidade" * i."CustoUnitario") AS "Custo"
            FROM "ItensVenda" i
            JOIN "Vendas" v ON v."Id" = i."VendaId"
            JOIN "Produtos" p ON p."Id" = i."ProdutoId"
            JOIN "Categorias" c ON c."Id" = p."CategoriaId"
            WHERE v."EmpresaId" = {db.EmpresaAtual} AND v."Status" = 'Concluida' AND v."DataHora" >= {ini} AND v."DataHora" < {fim}
            GROUP BY p."Id", p."Nome", c."Nome", p."Unidade"
            ORDER BY sum(i."Total") DESC
            """).ToListAsync();

        var total = linhas.Sum(l => l.Faturamento);

        // Curva ABC: do que mais fatura para o que menos fatura, somando a participação.
        // Até 80% do faturamento acumulado = A (os poucos que sustentam o mercado), até 95% = B, o resto = C.
        var acumulado = 0m;
        var produtos = linhas.Select(l =>
        {
            var participacao = total == 0 ? 0 : l.Faturamento / total * 100;
            var classe = acumulado < 80 ? "A" : acumulado < 95 ? "B" : "C"; // decide pelo acumulado ANTES deste item
            acumulado += participacao;
            return new ProdutoAbc(l.ProdutoId, l.Nome, l.Categoria, l.Unidade, l.Quantidade, l.Vendas, l.Faturamento,
                Math.Round(l.Faturamento - l.Custo, 2), Margem(l.Faturamento, l.Custo), Math.Round(participacao, 2), Math.Round(acumulado, 2), classe);
        }).ToList();

        var classes = produtos.GroupBy(p => p.Classe).OrderBy(g => g.Key)
            .Select(g => new ResumoAbc(g.Key, g.Count(), g.Sum(p => p.Faturamento), Math.Round(g.Sum(p => p.Participacao), 1)))
            .ToList();

        var categorias = linhas.GroupBy(l => l.Categoria)
            .Select(g => new CategoriaResumo(g.Key, g.Sum(l => l.Faturamento), Math.Round(g.Sum(l => l.Faturamento - l.Custo), 2),
                Margem(g.Sum(l => l.Faturamento), g.Sum(l => l.Custo)),
                total == 0 ? 0 : Math.Round(g.Sum(l => l.Faturamento) / total * 100, 1), g.Count()))
            .OrderByDescending(c => c.Faturamento)
            .ToList();

        return new RelatorioProdutos(periodo, classes, produtos, categorias);
    }

    // Clima: o clima influenciou as vendas?
    public async Task<RelatorioClima> Clima(DateOnly? de, DateOnly? ate)
    {
        var periodo = Ler(de, ate);
        var (ini, fim) = Utc(periodo);
        // O clima é o da CIDADE da loja (sem cidade cadastrada, não há o que cruzar: coordenadas impossíveis = nenhuma linha).
        var (lat, lon) = CoordenadasDoClima();
        var demonstracao = await db.Empresas.AsNoTracking().AnyAsync(e => e.Id == db.EmpresaAtual && e.Demonstracao);

        // Cada hora do período, com o clima e quantas vendas houve nela.
        var horas = await db.Database.SqlQuery<LinhaClimaHora>($"""
            SELECT c."DataHora" AS "Hora", c."Temperatura" AS "Temperatura", c."Chuva" AS "Chuva",
                   c."CodigoTempo" AS "CodigoTempo", count(v."Id")::int AS "Vendas"
            FROM "Clima" c
            LEFT JOIN "Vendas" v ON date_trunc('hour', v."DataHora") = c."DataHora" AND v."Status" = 'Concluida' AND v."EmpresaId" = {db.EmpresaAtual}
            WHERE c."DataHora" >= {ini} AND c."DataHora" < {fim} AND c."Latitude" = {lat} AND c."Longitude" = {lon}
            GROUP BY c."DataHora", c."Temperatura", c."Chuva", c."CodigoTempo"
            """).ToListAsync();

        // Só interessam as horas em que o mercado estava ABERTO. Na demonstração, o horário do simulador;
        // numa loja de verdade (cada uma tem o seu horário), as horas em que houve pelo menos uma venda.
        var abertas = horas.Where(h =>
        {
            if (!demonstracao) return h.Vendas > 0;
            var local = TimeZoneInfo.ConvertTime(h.Hora, Relogio.Fuso);
            return ComportamentoCliente.AbertoNaHora(DateOnly.FromDateTime(local.DateTime), local.Hour);
        }).ToList();

        var faturamentoPorHora = await FaturamentoPorHoraAsync(ini, fim);

        FaixaClima Faixa(string nome, IEnumerable<LinhaClimaHora> grupo)
        {
            var lista = grupo.ToList();
            var vendas = lista.Sum(h => h.Vendas);
            var faturamento = lista.Sum(h => faturamentoPorHora.GetValueOrDefault(h.Hora));
            return new FaixaClima(nome, lista.Count, lista.Count == 0 ? 0 : Math.Round((decimal)vendas / lista.Count, 1),
                vendas == 0 ? null : Math.Round(faturamento / vendas, 2));
        }

        var porTemperatura = new List<FaixaClima>
        {
            Faixa("Abaixo de 15 °C", abertas.Where(h => h.Temperatura < 15)),
            Faixa("15 a 20 °C", abertas.Where(h => h.Temperatura >= 15 && h.Temperatura < 20)),
            Faixa("20 a 24 °C", abertas.Where(h => h.Temperatura >= 20 && h.Temperatura < 24)),
            Faixa("24 °C ou mais", abertas.Where(h => h.Temperatura >= 24)),
        }.Where(f => f.Horas > 0).ToList();

        var porChuva = new List<FaixaClima>
        {
            Faixa("Sem chuva", abertas.Where(h => !Chovendo(h))),
            Faixa("Com chuva", abertas.Where(Chovendo)),
        };

        // Sensibilidade: em quantas vendas o produto aparece nas horas quentes vs frias (e com/sem chuva).
        var sens = await db.Database.SqlQuery<LinhaSensibilidade>($"""
            SELECT p."Nome" AS "Nome",
                   count(DISTINCT v."Id") FILTER (WHERE c."Temperatura" >= {Quente})::int AS "ComCalor",
                   count(DISTINCT v."Id") FILTER (WHERE c."Temperatura" <  {Frio})::int   AS "ComFrio",
                   count(DISTINCT v."Id") FILTER (WHERE c."Chuva" > 0.2 OR c."CodigoTempo" BETWEEN 51 AND 67
                                                     OR c."CodigoTempo" BETWEEN 80 AND 82 OR c."CodigoTempo" >= 95)::int AS "ComChuva",
                   count(DISTINCT v."Id") FILTER (WHERE NOT (c."Chuva" > 0.2 OR c."CodigoTempo" BETWEEN 51 AND 67
                                                     OR c."CodigoTempo" BETWEEN 80 AND 82 OR c."CodigoTempo" >= 95))::int AS "SemChuva"
            FROM "ItensVenda" i
            JOIN "Vendas" v ON v."Id" = i."VendaId"
            JOIN "Produtos" p ON p."Id" = i."ProdutoId"
            JOIN "Clima" c ON c."DataHora" = date_trunc('hour', v."DataHora") AND c."Latitude" = {lat} AND c."Longitude" = {lon}
            WHERE v."EmpresaId" = {db.EmpresaAtual} AND v."Status" = 'Concluida' AND v."DataHora" >= {ini} AND v."DataHora" < {fim}
            GROUP BY p."Nome"
            """).ToListAsync();

        // Total de vendas em cada condição (o "denominador" das porcentagens).
        var vendasCalor = abertas.Where(h => h.Temperatura >= Quente).Sum(h => h.Vendas);
        var vendasFrio = abertas.Where(h => h.Temperatura < Frio).Sum(h => h.Vendas);
        var vendasChuva = abertas.Where(Chovendo).Sum(h => h.Vendas);
        var vendasSeco = abertas.Where(h => !Chovendo(h)).Sum(h => h.Vendas);

        // "Fator" = quantas vezes MAIS o produto aparece numa condição do que na outra.
        // Ex.: café em 18% das compras no frio e em 6% no calor → fator 3,2.
        // Para não mostrar "padrões" que são só sorte, cada um passa por um TESTE ESTATÍSTICO (teste z de duas
        // proporções). Como testamos ~55 produtos × 3 condições de uma vez, com 95% de confiança (z ≥ 2) uns 5%
        // dariam "positivo" por puro acaso (o problema das COMPARAÇÕES MÚLTIPLAS: esponja "subindo na chuva").
        // Por isso exigimos z ≥ 3 (~99,7%), na linha da correção de Bonferroni.
        List<ProdutoSensivel> Top(Func<LinhaSensibilidade, (int a, int b)> par, int totalA, int totalB, string textoA, string textoB) =>
            sens.Select(s => (s.Nome, par(s).a, par(s).b))
                .Where(x => totalA > 0 && totalB > 0 && x.a + x.b > 0)
                .Select(x =>
                {
                    double p1 = (double)x.a / totalA, p2 = (double)x.b / totalB;
                    var juntos = (double)(x.a + x.b) / (totalA + totalB);
                    var erro = Math.Sqrt(juntos * (1 - juntos) * (1.0 / totalA + 1.0 / totalB));
                    var z = erro == 0 ? 0 : (p1 - p2) / erro;
                    var fator = p2 == 0 ? 9.9m : Math.Round((decimal)(p1 / p2), 1);
                    return (Produto: new ProdutoSensivel(x.Nome, fator,
                        $"em {p1 * 100:0.#}% das compras {textoA} e {p2 * 100:0.#}% {textoB}"), z);
                })
                .Where(x => x.z >= ZMinimo && x.Produto.Fator >= 1.2m)
                .OrderByDescending(x => x.Produto.Fator).Take(5).Select(x => x.Produto).ToList();

        return new RelatorioClima(periodo, porTemperatura, porChuva,
            Top(s => (s.ComCalor, s.ComFrio), vendasCalor, vendasFrio, $"com {Quente:0} °C ou mais", $"abaixo de {Frio:0} °C"),
            Top(s => (s.ComFrio, s.ComCalor), vendasFrio, vendasCalor, $"abaixo de {Frio:0} °C", $"com {Quente:0} °C ou mais"),
            Top(s => (s.ComChuva, s.SemChuva), vendasChuva, vendasSeco, "com chuva", "sem chuva"));
    }

    // ------------------------------------------------------------------ apoio

    private async Task<List<LinhaDia>> PorDiaAsync(DateOnly de, DateOnly ate)
    {
        var (ini, fim) = (Relogio.InicioDoDiaUtc(de), Relogio.InicioDoDiaUtc(ate.AddDays(1)));
        var (lat, lon) = CoordenadasDoClima();
        // generate_series cria TODOS os dias do período: dia sem venda aparece com zero (e não "some" do gráfico).
        return await db.Database.SqlQuery<LinhaDia>($"""
            WITH dias AS (
                SELECT generate_series({de}::date, {ate}::date, interval '1 day')::date AS d
            ), vendas AS (
                SELECT (v."DataHora" AT TIME ZONE {Relogio.FusoId})::date AS d,
                       count(*) AS qtd, sum(v."Total") AS total,
                       sum((SELECT sum(i."Quantidade" * i."CustoUnitario") FROM "ItensVenda" i WHERE i."VendaId" = v."Id")) AS custo
                FROM "Vendas" v
                WHERE v."EmpresaId" = {db.EmpresaAtual} AND v."Status" = 'Concluida' AND v."DataHora" >= {ini} AND v."DataHora" < {fim}
                GROUP BY 1
            ), clima AS (
                SELECT ("DataHora" AT TIME ZONE {Relogio.FusoId})::date AS d,
                       max("Temperatura") AS tmax, min("Temperatura") AS tmin, sum("Chuva") AS chuva
                FROM "Clima" WHERE "DataHora" >= {ini} AND "DataHora" < {fim} AND "Latitude" = {lat} AND "Longitude" = {lon}
                GROUP BY 1
            )
            SELECT dias.d AS "Data", coalesce(vendas.qtd, 0)::int AS "Vendas",
                   coalesce(vendas.total, 0) AS "Faturamento", coalesce(vendas.custo, 0) AS "Custo",
                   clima.tmax AS "TempMax", clima.tmin AS "TempMin", coalesce(clima.chuva, 0) AS "Chuva"
            FROM dias
            LEFT JOIN vendas ON vendas.d = dias.d
            LEFT JOIN clima ON clima.d = dias.d
            ORDER BY dias.d
            """).ToListAsync();
    }

    private async Task<Dictionary<DateTimeOffset, decimal>> FaturamentoPorHoraAsync(DateTimeOffset ini, DateTimeOffset fim)
    {
        var linhas = await db.Database.SqlQuery<LinhaValorHora>($"""
            SELECT date_trunc('hour', v."DataHora") AS "Hora", sum(v."Total") AS "Valor"
            FROM "Vendas" v
            WHERE v."EmpresaId" = {db.EmpresaAtual} AND v."Status" = 'Concluida' AND v."DataHora" >= {ini} AND v."DataHora" < {fim}
            GROUP BY 1
            """).ToListAsync();
        return linhas.ToDictionary(l => l.Hora, l => l.Valor);
    }

    private static Indicadores CalcularIndicadores(List<LinhaDia> atual, List<LinhaDia> anterior, int diasAbertos)
    {
        decimal Fat(List<LinhaDia> d) => d.Sum(x => x.Faturamento);
        int Qtd(List<LinhaDia> d) => d.Sum(x => x.Vendas);
        decimal Ticket(List<LinhaDia> d) => Qtd(d) == 0 ? 0 : Fat(d) / Qtd(d);
        decimal? Variacao(decimal agora, decimal antes) => antes == 0 ? null : Math.Round((agora - antes) / antes * 100, 1);

        var custo = atual.Sum(x => x.Custo);
        return new Indicadores(
            Fat(atual), Qtd(atual), Math.Round(Ticket(atual), 2), Math.Round(Fat(atual) - custo, 2), Margem(Fat(atual), custo),
            diasAbertos == 0 ? 0 : Math.Round(Fat(atual) / diasAbertos, 2), // média por dia ABERTO
            Variacao(Fat(atual), Fat(anterior)), Variacao(Qtd(atual), Qtd(anterior)), Variacao(Ticket(atual), Ticket(anterior)));
    }

    public static Periodo Ler(DateOnly? de, DateOnly? ate)
    {
        var fim = ate ?? Relogio.Hoje;
        var inicio = de ?? fim.AddDays(-29);
        if (inicio > fim) (inicio, fim) = (fim, inicio);
        if (fim.DayNumber - inicio.DayNumber > 366) inicio = fim.AddDays(-366); // limite: 1 ano
        return new Periodo(inicio, fim, fim.DayNumber - inicio.DayNumber + 1);
    }

    private static (DateTimeOffset, DateTimeOffset) Utc(Periodo p) =>
        (Relogio.InicioDoDiaUtc(p.De), Relogio.InicioDoDiaUtc(p.Ate.AddDays(1)));

    private static decimal Margem(decimal faturamento, decimal custo) =>
        faturamento == 0 ? 0 : Math.Round((faturamento - custo) / faturamento * 100, 1);

    private (decimal Lat, decimal Lon) CoordenadasDoClima() =>
        locais.Local(db.EmpresaAtual) is { } l ? (l.LatitudeClima, l.LongitudeClima) : (999m, 999m);

    private static bool Chovendo(LinhaClimaHora h) =>
        h.Chuva > 0.2m || h.CodigoTempo is >= 51 and <= 67 or >= 80 and <= 82 or >= 95;
}
