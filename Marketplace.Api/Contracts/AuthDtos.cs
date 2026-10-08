using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// DTOs = "formatos" dos dados que entram e saem da API (nunca expomos a entidade Usuario inteira).
public record LoginRequest(string Email, string Senha);

public record UsuarioLogadoResponse(string Id, string Nome, string Email, IList<string> Perfis, bool TrocarSenha);

public record TrocarSenhaRequest(
    [Required(ErrorMessage = "Informe a senha atual.")] string SenhaAtual,
    [Required(ErrorMessage = "Informe a nova senha.")] string NovaSenha);

// ----- Administração de usuários -----
public record UsuarioResponse(
    string Id, string Nome, string Email, string Perfil, bool Ativo, bool TrocarSenha, bool Sistema,
    DateTimeOffset CriadoEm, DateTimeOffset? UltimoAcessoEm);

public record NovoUsuarioRequest(
    [Required(ErrorMessage = "Informe o nome.")] [StringLength(150, MinimumLength = 3, ErrorMessage = "Nome de 3 a 150 caracteres.")] string Nome,
    [Required(ErrorMessage = "Informe o e-mail.")] [EmailAddress(ErrorMessage = "E-mail inválido.")] string Email,
    [Required(ErrorMessage = "Escolha o perfil.")] string Perfil);

public record EditarUsuarioRequest(
    [Required(ErrorMessage = "Informe o nome.")] [StringLength(150, MinimumLength = 3, ErrorMessage = "Nome de 3 a 150 caracteres.")] string Nome,
    [Required(ErrorMessage = "Escolha o perfil.")] string Perfil);

// A senha provisória aparece UMA vez, para o administrador repassar ao funcionário.
public record SenhaProvisoriaResponse(UsuarioResponse Usuario, string SenhaProvisoria);
