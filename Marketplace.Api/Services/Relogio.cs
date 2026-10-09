namespace Marketplace.Api.Services;

// O servidor guarda horários em UTC; "hoje", para o mercado, é o dia NO FUSO DA LOJA (o Brasil tem 4 fusos:
// Manaus é 1 h a menos que Brasília). O fuso vem da empresa da requisição: o ContextoEmpresa chama UsarFuso()
// e o valor vale para todo o resto daquela requisição (ou daquele trabalho em segundo plano) — AsyncLocal.
// Sem empresa definida (ex.: tela de login), vale o horário de Brasília.
public static class Relogio
{
    public const string FusoPadrao = "America/Sao_Paulo";
    private static readonly TimeZoneInfo Padrao = TimeZoneInfo.FindSystemTimeZoneById(FusoPadrao);
    private static readonly AsyncLocal<TimeZoneInfo?> FusoAtual = new();

    public static TimeZoneInfo Fuso => FusoAtual.Value ?? Padrao;
    public static string FusoId => Fuso.Id; // ex.: "America/Manaus" (também serve para o AT TIME ZONE do PostgreSQL)

    public static void UsarFuso(string? id)
    {
        try { FusoAtual.Value = string.IsNullOrWhiteSpace(id) ? null : TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { FusoAtual.Value = null; }
    }

    public static DateTimeOffset Agora => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso);

    public static DateOnly Hoje => DateOnly.FromDateTime(Agora.DateTime);

    // Em que DIA (no calendário da loja) aconteceu um momento qualquer (ex.: a emissão de uma nota).
    public static DateOnly DiaLocal(DateTimeOffset momento) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(momento, Fuso).DateTime);

    // Meia-noite da loja naquele dia, convertida para UTC (o PostgreSQL/Npgsql só aceita UTC).
    // Ex.: 08/10 00:00 em Brasília = 08/10 03:00 UTC; em Manaus = 08/10 04:00 UTC.
    public static DateTimeOffset InicioDoDiaUtc(DateOnly dia)
    {
        var meiaNoite = dia.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(meiaNoite, Fuso.GetUtcOffset(meiaNoite)).ToUniversalTime();
    }
}
