namespace Marketplace.Api.Contracts;

// DTOs = "formatos" dos dados que entram e saem da API (nunca expomos a entidade Usuario inteira).
public record LoginRequest(string Email, string Senha);

public record UsuarioLogadoResponse(string Id, string Nome, string Email, IList<string> Perfis);
