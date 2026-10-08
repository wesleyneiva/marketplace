using Microsoft.AspNetCore.Identity;

namespace Marketplace.Api.Models;

// Usuário do sistema (funcionário do mercado).
// Herda do IdentityUser, que já traz e-mail, senha (guardada como hash), bloqueio por tentativas etc.
// Aqui só acrescentamos o que é nosso.
public class Usuario : IdentityUser
{
    public string NomeCompleto { get; set; } = "";

    // Desativado = não entra mais (e quem estava logado cai em até 1 minuto). Nunca apagamos um usuário:
    // as vendas e movimentações antigas continuam apontando para ele.
    public bool Ativo { get; set; } = true;

    // Senha provisória (criada ou redefinida pelo administrador): obriga a trocar no próximo acesso.
    public bool TrocarSenha { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UltimoAcessoEm { get; set; }
}
