namespace Marketplace.Api.Models;

// Os perfis (roles) do sistema. Usar as constantes evita erro de digitação
// em [Authorize(Roles = Perfis.Administrador)].
public static class Perfis
{
    public const string Administrador = "Administrador";
    public const string Gerente = "Gerente";
    public const string Caixa = "Caixa";

    public static readonly string[] Todos = [Administrador, Gerente, Caixa];
}
