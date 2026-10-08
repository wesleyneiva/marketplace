namespace Marketplace.Api.Services;

// O servidor guarda horários em UTC; "hoje", para o mercado, é o dia em Brasília.
public static class Relogio
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTimeOffset AgoraBrasilia => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brasilia);

    public static DateOnly HojeBrasilia => DateOnly.FromDateTime(AgoraBrasilia.DateTime);
}
