namespace Marketplace.Api.Services;

// O servidor guarda horários em UTC; "hoje", para o mercado, é o dia em Brasília.
public static class Relogio
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTimeOffset AgoraBrasilia => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brasilia);

    public static DateOnly HojeBrasilia => DateOnly.FromDateTime(AgoraBrasilia.DateTime);

    // Meia-noite de Brasília daquele dia, convertida para UTC (o PostgreSQL/Npgsql só aceita UTC).
    // Ex.: 08/10 00:00 em Brasília = 08/10 03:00 UTC.
    public static DateTimeOffset InicioDoDiaUtc(DateOnly dia)
    {
        var meiaNoite = dia.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(meiaNoite, Brasilia.GetUtcOffset(meiaNoite)).ToUniversalTime();
    }
}
