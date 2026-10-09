namespace Marketplace.Api.Services;

// O servidor guarda horários em UTC; "hoje", para o mercado, é o dia em Brasília.
public static class Relogio
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTimeOffset AgoraBrasilia => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brasilia);

    public static DateOnly HojeBrasilia => DateOnly.FromDateTime(AgoraBrasilia.DateTime);

    // Em que DIA (no calendário de Brasília) aconteceu um momento qualquer (ex.: a emissão de uma nota).
    public static DateOnly DiaBrasilia(DateTimeOffset momento) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(momento, Brasilia).DateTime);

    // Meia-noite de Brasília daquele dia, convertida para UTC (o PostgreSQL/Npgsql só aceita UTC).
    // Ex.: 08/10 00:00 em Brasília = 08/10 03:00 UTC.
    public static DateTimeOffset InicioDoDiaUtc(DateOnly dia)
    {
        var meiaNoite = dia.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(meiaNoite, Brasilia.GetUtcOffset(meiaNoite)).ToUniversalTime();
    }
}
