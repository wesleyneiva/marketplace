using Microsoft.AspNetCore.Identity;

namespace Marketplace.Api.Models;

// Usuário do sistema (funcionário do mercado).
// Herda do IdentityUser, que já traz e-mail, senha (guardada como hash), bloqueio por tentativas etc.
// Aqui só acrescentamos o que é nosso.
public class Usuario : IdentityUser
{
    public string NomeCompleto { get; set; } = "";
}
