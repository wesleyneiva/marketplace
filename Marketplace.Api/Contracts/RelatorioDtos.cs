namespace Marketplace.Api.Contracts;

// ----- Linhas que vêm direto do SQL (os nomes das propriedades = os "AS" das consultas) -----
public class LinhaDia
{
    public DateOnly Data { get; set; }
    public int Vendas { get; set; }
    public decimal Faturamento { get; set; }
    public decimal Custo { get; set; }
    public decimal? TempMax { get; set; }
    public decimal? TempMin { get; set; }
    public decimal Chuva { get; set; }
}

public class LinhaHora
{
    public int DiaSemana { get; set; } // 0 = domingo … 6 = sábado
    public int Hora { get; set; }
    public int Vendas { get; set; }
}

public class LinhaForma
{
    public string Forma { get; set; } = "";
    public decimal Valor { get; set; }
}

public class LinhaValorHora
{
    public DateTimeOffset Hora { get; set; }
    public decimal Valor { get; set; }
}

public class LinhaProduto
{
    public int ProdutoId { get; set; }
    public string Nome { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Unidade { get; set; } = "";
    public decimal Quantidade { get; set; }
    public int Vendas { get; set; }
    public decimal Faturamento { get; set; }
    public decimal Custo { get; set; }
}

public class LinhaClimaHora
{
    public DateTimeOffset Hora { get; set; }
    public decimal Temperatura { get; set; }
    public decimal Chuva { get; set; }
    public int CodigoTempo { get; set; }
    public int Vendas { get; set; }
}

public class LinhaSensibilidade
{
    public string Nome { get; set; } = "";
    public int ComCalor { get; set; }
    public int ComFrio { get; set; }
    public int ComChuva { get; set; }
    public int SemChuva { get; set; }
}

// ----- Respostas -----
public record Periodo(DateOnly De, DateOnly Ate, int Dias);

public record Indicadores(
    decimal Faturamento, int Vendas, decimal TicketMedio, decimal LucroBruto, decimal MargemPercentual,
    decimal FaturamentoMedioDia,
    decimal? VariacaoFaturamento, decimal? VariacaoVendas, decimal? VariacaoTicket); // % contra o período anterior

public record VendaDia(DateOnly Data, int Vendas, decimal Faturamento, decimal Lucro, decimal? TempMax, decimal? TempMin, decimal Chuva);

public record MediaDiaSemana(int Dia, string Nome, decimal Vendas, decimal Faturamento);

public record CelulaCalor(int DiaSemana, int Hora, decimal MediaVendas);

public record RelatorioVendas(
    Periodo Periodo, Indicadores Indicadores, IReadOnlyList<VendaDia> PorDia,
    IReadOnlyList<MediaDiaSemana> PorDiaSemana, IReadOnlyList<CelulaCalor> MapaDeCalor, IReadOnlyList<LinhaForma> PorForma);

public record ProdutoAbc(
    int ProdutoId, string Nome, string Categoria, string Unidade, decimal Quantidade, int Vendas,
    decimal Faturamento, decimal Lucro, decimal MargemPercentual, decimal Participacao, decimal Acumulado, string Classe);

public record CategoriaResumo(string Categoria, decimal Faturamento, decimal Lucro, decimal MargemPercentual, decimal Participacao, int Produtos);

public record ResumoAbc(string Classe, int Produtos, decimal Faturamento, decimal Participacao);

public record RelatorioProdutos(Periodo Periodo, IReadOnlyList<ResumoAbc> Classes, IReadOnlyList<ProdutoAbc> Produtos, IReadOnlyList<CategoriaResumo> Categorias);

public record FaixaClima(string Faixa, int Horas, decimal ClientesPorHora, decimal? TicketMedio);

public record ProdutoSensivel(string Nome, decimal Fator, string Explicacao);

public record RelatorioClima(
    Periodo Periodo, IReadOnlyList<FaixaClima> PorTemperatura, IReadOnlyList<FaixaClima> PorChuva,
    IReadOnlyList<ProdutoSensivel> SobemNoCalor, IReadOnlyList<ProdutoSensivel> SobemNoFrio, IReadOnlyList<ProdutoSensivel> SobemNaChuva);
