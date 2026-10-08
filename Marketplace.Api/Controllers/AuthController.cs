using Marketplace.Api.Contracts;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

// Login com cookie: a API devolve um cookie "HttpOnly" (o JavaScript não consegue ler,
// o navegador só o envia de volta). Assim não guardamos token nenhum no navegador.
[ApiController]
[Route("api/auth")]
public class AuthController(SignInManager<Usuario> signInManager, UserManager<Usuario> userManager) : ControllerBase
{
    // POST /api/auth/login   { "email": "...", "senha": "..." }
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var usuario = await userManager.FindByEmailAsync(request.Email);
        if (usuario is null)
            return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });

        // Primeiro só CONFERE a senha (sem logar). lockoutOnFailure: 5 senhas erradas → bloqueio de 15 minutos.
        var resultado = await signInManager.CheckPasswordSignInAsync(usuario, request.Senha, lockoutOnFailure: true);

        if (resultado.IsLockedOut)
            return Unauthorized(new { mensagem = "Muitas tentativas erradas. Conta bloqueada por 15 minutos." });

        if (!resultado.Succeeded)
            return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });

        // Só depois da senha certa dizemos que a conta está desativada (senão qualquer um descobriria quem existe).
        if (!usuario.Ativo)
            return StatusCode(StatusCodes.Status403Forbidden, new { mensagem = "Usuário desativado. Fale com o administrador." });

        await signInManager.SignInAsync(usuario, isPersistent: false);
        usuario.UltimoAcessoEm = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(usuario);

        return Ok(await MontarRespostaAsync(usuario));
    }

    // POST /api/auth/trocar-senha  { "senhaAtual": "...", "novaSenha": "..." }
    [Authorize]
    [HttpPost("trocar-senha")]
    public async Task<IActionResult> TrocarSenha(TrocarSenhaRequest request)
    {
        var usuario = await userManager.GetUserAsync(User);
        if (usuario is null)
            return Unauthorized();
        if (request.NovaSenha == request.SenhaAtual)
            return BadRequest(new { mensagem = "A nova senha precisa ser diferente da atual." });

        var resultado = await userManager.ChangePasswordAsync(usuario, request.SenhaAtual, request.NovaSenha);
        if (!resultado.Succeeded)
            return BadRequest(new { mensagem = string.Join(" ", resultado.Errors.Select(TraduzirErro).Distinct()) });

        usuario.TrocarSenha = false;
        await userManager.UpdateAsync(usuario);
        await signInManager.RefreshSignInAsync(usuario); // a troca muda o "carimbo" de segurança: renova o cookie
        return Ok(await MontarRespostaAsync(usuario));
    }

    // Mensagens do Identity em português (as padrão vêm em inglês).
    public static string TraduzirErro(IdentityError e) => e.Code switch
    {
        "PasswordMismatch" => "A senha atual está errada.",
        "PasswordTooShort" => "A senha precisa ter pelo menos 8 caracteres.",
        "PasswordRequiresDigit" => "A senha precisa ter pelo menos um número.",
        "PasswordRequiresLower" => "A senha precisa ter pelo menos uma letra minúscula.",
        "PasswordRequiresUpper" => "A senha precisa ter pelo menos uma letra maiúscula.",
        "PasswordRequiresNonAlphanumeric" => "A senha precisa ter pelo menos um símbolo (ex.: ! @ # -).",
        "DuplicateEmail" or "DuplicateUserName" => "Já existe um usuário com este e-mail.",
        "InvalidEmail" => "E-mail inválido.",
        _ => e.Description,
    };

    // POST /api/auth/logout
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    // GET /api/auth/eu → quem está logado (o Angular usa isso ao abrir a página).
    [Authorize]
    [HttpGet("eu")]
    public async Task<IActionResult> Eu()
    {
        var usuario = await userManager.GetUserAsync(User);
        if (usuario is null || !usuario.Ativo)
        {
            await signInManager.SignOutAsync();
            return Unauthorized();
        }

        return Ok(await MontarRespostaAsync(usuario));
    }

    private async Task<UsuarioLogadoResponse> MontarRespostaAsync(Usuario usuario) =>
        new(usuario.Id, usuario.NomeCompleto, usuario.Email!, await userManager.GetRolesAsync(usuario), usuario.TrocarSenha);
}
