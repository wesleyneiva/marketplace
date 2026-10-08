using System.Globalization;
using System.Text;
using Marketplace.Api.Models;

namespace Marketplace.Api.Services.Simulador;

// "Etiquetas" de comportamento: dizem como o produto reage ao clima, ao horário e ao dia.
[Flags]
public enum Etiqueta
{
    Nenhuma = 0,
    Gelado = 1,       // vende mais no calor (cerveja, água, refrigerante)
    Quente = 2,       // vende mais no frio (café, achocolatado, leite)
    CafeDaManha = 4,  // vende mais de manhã (pão, leite, manteiga)
    Churrasco = 8,    // vende mais no fim de semana e no fim da tarde (carne, linguiça, cerveja)
    DiaDeChuva = 16,  // vende mais com chuva (pão, café, bolo)
}

// O que se sabe de cada produto para simular: popularidade, etiquetas e quanto se leva.
public record Habito(double Popularidade, Etiqueta Etiquetas, decimal PesoMin = 0, decimal PesoMax = 0, int[]? Pacotes = null);

// Regras do "jeito de comprar" dos clientes do mercadinho.
// A busca é por PALAVRA no nome do produto (sem acento): "pao frances" pega "Pão francês".
public static class PerfilConsumo
{
    private static readonly (string Palavra, Habito Habito)[] Regras =
    [
        ("pao frances", new(6.0, Etiqueta.CafeDaManha | Etiqueta.DiaDeChuva, 0.15m, 0.90m)),
        ("pao de forma", new(1.4, Etiqueta.CafeDaManha)),
        ("bolo", new(0.8, Etiqueta.DiaDeChuva)),
        ("leite integral", new(3.0, Etiqueta.CafeDaManha | Etiqueta.Quente, Pacotes: [1, 1, 2, 2, 3, 6])),
        ("leite condensado", new(0.7, Etiqueta.DiaDeChuva)),
        ("cafe", new(1.6, Etiqueta.Quente | Etiqueta.CafeDaManha | Etiqueta.DiaDeChuva)),
        ("achocolatado", new(0.9, Etiqueta.Quente)),
        ("manteiga", new(1.0, Etiqueta.CafeDaManha)),
        ("requeijao", new(0.9, Etiqueta.CafeDaManha)),
        ("iogurte", new(1.2, Etiqueta.Nenhuma, Pacotes: [1, 1, 2, 3, 4])),
        ("queijo", new(1.2, Etiqueta.CafeDaManha, 0.10m, 0.45m)),
        ("presunto", new(1.1, Etiqueta.CafeDaManha, 0.10m, 0.40m)),
        ("ovos", new(1.3, Etiqueta.CafeDaManha)),
        ("cerveja pilsen", new(2.2, Etiqueta.Gelado | Etiqueta.Churrasco, Pacotes: [1, 2, 3, 6, 6, 12])),
        ("cerveja puro malte", new(1.2, Etiqueta.Gelado | Etiqueta.Churrasco, Pacotes: [1, 2, 4, 6])),
        ("agua mineral", new(2.0, Etiqueta.Gelado, Pacotes: [1, 1, 2, 3, 6])),
        ("agua de coco", new(0.8, Etiqueta.Gelado)),
        ("refrigerante", new(1.8, Etiqueta.Gelado | Etiqueta.Churrasco, Pacotes: [1, 1, 2])),
        ("energetico", new(0.6, Etiqueta.Gelado)),
        ("suco", new(0.7, Etiqueta.Gelado)),
        ("banana", new(1.8, Etiqueta.Nenhuma, 0.50m, 2.20m)),
        ("tomate", new(1.5, Etiqueta.Churrasco, 0.30m, 1.50m)),
        ("batata", new(1.2, Etiqueta.Nenhuma, 0.50m, 2.50m)),
        ("cebola", new(1.2, Etiqueta.Churrasco, 0.30m, 1.20m)),
        // "macarrao" ANTES de "maca": senão o macarrão cairia na regra da maçã (a busca é por pedaço do nome).
        ("macarrao", new(1.2, Etiqueta.DiaDeChuva, Pacotes: [1, 1, 2])),
        ("maca", new(1.1, Etiqueta.Nenhuma, 0.40m, 1.60m)),
        ("laranja", new(1.0, Etiqueta.Nenhuma, 1.00m, 3.00m)),
        ("alface", new(1.0, Etiqueta.Nenhuma, Pacotes: [1, 1, 2])),
        ("carne moida", new(1.2, Etiqueta.Nenhuma, 0.40m, 1.50m)),
        ("peito de frango", new(1.3, Etiqueta.Nenhuma, 0.60m, 2.00m)),
        ("coxa", new(0.9, Etiqueta.Churrasco, 0.80m, 2.00m)),
        ("linguica", new(1.0, Etiqueta.Churrasco, 0.40m, 1.30m)),
        ("costela", new(0.9, Etiqueta.Churrasco, 1.00m, 3.00m)),
        ("arroz", new(1.4, Etiqueta.Nenhuma)),
        ("feijao", new(1.3, Etiqueta.Nenhuma)),
        ("acucar", new(1.0, Etiqueta.Nenhuma)),
        ("oleo", new(1.0, Etiqueta.Nenhuma)),
        ("molho de tomate", new(1.1, Etiqueta.DiaDeChuva, Pacotes: [1, 2])),
        ("biscoito", new(1.0, Etiqueta.DiaDeChuva)),
    ];

    // Produtos sem regra própria (limpeza, higiene...): saem pouco por compra num mercadinho.
    private static readonly Habito Padrao = new(0.35, Etiqueta.Nenhuma);

    public static Habito Para(Produto produto)
    {
        var nome = SemAcento(produto.Nome);
        foreach (var (palavra, habito) in Regras)
            if (nome.Contains(palavra))
                return habito;

        // Por quilo sem regra: leva de 300 g a 1,5 kg.
        return produto.Unidade is Unidades.Quilo or Unidades.Litro ? Padrao with { PesoMin = 0.3m, PesoMax = 1.5m } : Padrao;
    }

    // Quanto o momento (clima, hora, dia) aumenta ou diminui a vontade de comprar este produto.
    public static double Multiplicador(Habito h, ClimaAgora clima, DateTimeOffset agora)
    {
        var m = 1.0;
        var hora = agora.Hour;
        var fimDeSemana = agora.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        if (h.Etiquetas.HasFlag(Etiqueta.Gelado)) m *= clima.Quente ? 2.5 : clima.Frio ? 0.5 : 1.0;
        if (h.Etiquetas.HasFlag(Etiqueta.Quente)) m *= clima.Frio ? 1.8 : clima.Quente ? 0.6 : 1.0;
        if (h.Etiquetas.HasFlag(Etiqueta.CafeDaManha)) m *= hora < 10 ? 2.2 : hora >= 17 ? 1.3 : 0.8;
        if (h.Etiquetas.HasFlag(Etiqueta.DiaDeChuva) && clima.Chovendo) m *= 1.4;
        if (h.Etiquetas.HasFlag(Etiqueta.Churrasco))
        {
            if (fimDeSemana) m *= 2.2;                     // sábado e domingo: churrasco!
            if (agora.DayOfWeek == DayOfWeek.Friday && hora >= 16) m *= 1.8;
            if (clima.Chovendo) m *= 0.7;                  // chuva atrapalha o churrasco
        }
        return m;
    }

    public static string SemAcento(string texto)
    {
        var decomposto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}
