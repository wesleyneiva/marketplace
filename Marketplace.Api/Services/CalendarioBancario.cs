namespace Marketplace.Api.Services;

// Dias úteis BANCÁRIOS (o dinheiro da maquininha só cai neles): sem sábado, domingo e feriado nacional,
// inclusive os móveis (Carnaval, Sexta-feira Santa e Corpus Christi, que dependem da Páscoa).
public static class CalendarioBancario
{
    private static readonly HashSet<(int Dia, int Mes)> Fixos =
        [(1, 1), (21, 4), (1, 5), (7, 9), (12, 10), (2, 11), (15, 11), (20, 11), (25, 12)];

    public static bool DiaUtil(DateOnly dia)
    {
        if (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (Fixos.Contains((dia.Day, dia.Month))) return false;
        var pascoa = Pascoa(dia.Year);
        return dia != pascoa.AddDays(-48) && dia != pascoa.AddDays(-47) // Carnaval (segunda e terça)
            && dia != pascoa.AddDays(-2)                                  // Sexta-feira Santa
            && dia != pascoa.AddDays(60);                                 // Corpus Christi
    }

    public static DateOnly ProximoDiaUtil(DateOnly dia)
    {
        while (!DiaUtil(dia)) dia = dia.AddDays(1);
        return dia;
    }

    // Quando a maquininha deposita: D+prazo (contando só dias úteis, se for o caso). Se cair em dia sem
    // expediente bancário, vai para o próximo dia útil — como as maquininhas fazem.
    public static DateOnly Previsao(DateOnly venda, int prazoDias, bool diasUteis)
    {
        if (prazoDias <= 0) return venda;
        var dia = venda;
        if (diasUteis)
            for (var i = 0; i < prazoDias; i++) dia = ProximoDiaUtil(dia.AddDays(1));
        else
            dia = venda.AddDays(prazoDias);
        return ProximoDiaUtil(dia);
    }

    // Domingo de Páscoa (algoritmo de Meeus/Jones/Butcher, calendário gregoriano).
    public static DateOnly Pascoa(int ano)
    {
        int a = ano % 19, b = ano / 100, c = ano % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451, mes = (h + l - 7 * m + 114) / 31, dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(ano, mes, dia);
    }
}
