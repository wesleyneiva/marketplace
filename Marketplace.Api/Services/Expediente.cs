using Marketplace.Api.Models;

namespace Marketplace.Api.Services;

// "A loja está aberta?" — pelo horário de funcionamento da EMPRESA da requisição (ou do trabalho em segundo plano).
// Mesmo esquema do Relogio: o ContextoEmpresa chama Usar() e o horário vale até o fim daquela requisição (AsyncLocal).
// Sem empresa definida, vale o horário padrão (seg–sáb 8h–19h).
public static class Expediente
{
    private static readonly HorarioFuncionamento Padrao = HorarioFuncionamento.Padrao();
    private static readonly AsyncLocal<HorarioFuncionamento?> HorarioAtual = new();

    public static HorarioFuncionamento Horario => HorarioAtual.Value ?? Padrao;

    public static void Usar(HorarioFuncionamento? horario) => HorarioAtual.Value = horario;

    // Nome do feriado (nacional ou da cidade), ou null.
    public static string? Feriado(DateOnly dia) =>
        CalendarioBancario.FeriadoNacional(dia)
        ?? Horario.FeriadosLocais.FirstOrDefault(f => f.Dia == dia.Day && f.Mes == dia.Month)?.Nome;

    // O horário que vale NAQUELE dia (feriado conta como domingo ou como dia normal, conforme a regra), ou null = fechado.
    public static HorarioDia? DoDia(DateOnly dia)
    {
        var h = Horario;
        if (h.Dias.Count != 7) h = Padrao;
        var horario = Feriado(dia) is null ? h.Dias[(int)dia.DayOfWeek]
            : h.Feriados switch
            {
                RegrasFeriado.Domingo => h.Dias[0],
                RegrasFeriado.Normal => h.Dias[(int)dia.DayOfWeek],
                _ => null,
            };
        return horario is { Aberto: true } && horario.Fecha > horario.Abre ? horario : null;
    }

    public static bool AbertoNoDia(DateOnly dia) => DoDia(dia) is not null;

    // Aberto em ALGUMA parte daquela hora (ex.: abre 7:30 → a hora 7 conta). Para relatórios por hora.
    public static bool AbertoNaHora(DateOnly dia, int hora) =>
        DoDia(dia) is { } h && Minutos(h.Abre) < (hora + 1) * 60 && Minutos(h.Fecha) > hora * 60;

    private static int Minutos(TimeOnly t) => t.Hour * 60 + t.Minute;

    // Aberto neste exato momento (horário local da loja). Para o simulador.
    public static bool AbertoAgora(DateTimeOffset agoraLocal)
    {
        var h = DoDia(DateOnly.FromDateTime(agoraLocal.DateTime));
        var t = TimeOnly.FromDateTime(agoraLocal.DateTime);
        return h is not null && t >= h.Abre && t < h.Fecha;
    }

    // Para textos (insights da IA): "seg–sáb 8h–21h; domingo fechado; feriados: fechado".
    public static string Descricao()
    {
        var h = Horario.Dias.Count == 7 ? Horario : Padrao;
        string[] nomes = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];
        var ordem = new[] { 1, 2, 3, 4, 5, 6, 0 }; // começa na segunda
        var partes = new List<string>();
        for (var i = 0; i < ordem.Length;)
        {
            var dia = h.Dias[ordem[i]];
            var j = i;
            while (j + 1 < ordem.Length && Igual(h.Dias[ordem[j + 1]], dia)) j++;
            var faixa = i == j ? nomes[ordem[i]] : $"{nomes[ordem[i]]}–{nomes[ordem[j]]}";
            partes.Add(dia.Aberto ? $"{faixa} {Hora(dia.Abre)}–{Hora(dia.Fecha)}" : $"{faixa} fechado");
            i = j + 1;
        }
        var feriados = h.Feriados switch
        {
            RegrasFeriado.Domingo => "feriados: horário de domingo",
            RegrasFeriado.Normal => "feriados: abre normalmente",
            _ => "feriados: fechado",
        };
        return $"{string.Join("; ", partes)}; {feriados}";

        static bool Igual(HorarioDia a, HorarioDia b) => a.Aberto == b.Aberto && (!a.Aberto || (a.Abre == b.Abre && a.Fecha == b.Fecha));
    }

    public static string Hora(TimeOnly t) => t.Minute == 0 ? $"{t.Hour}h" : $"{t.Hour}h{t.Minute:00}";
}
