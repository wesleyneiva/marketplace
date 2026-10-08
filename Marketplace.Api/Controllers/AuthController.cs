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

        // lockoutOnFailure: true → depois de 5 senhas erradas, a conta fica bloqueada por 15 minutos.
        var resultado = await signInManager.PasswordSignInAsync(usuario, request.Senha,
            isPersistent: false, lockoutOnFailure: true);

        if (resultado.IsLockedOut)
            return Unauthorized(new { mensagem = "Muitas tentativas erradas. Conta bloqueada por 15 minutos." });

        if (!resultado.Succeeded)
            return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });

        return Ok(await MontarRespostaAsync(usuario));
    }

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
        if (usuario is null)
            return Unauthorized();

        return Ok(await MontarRespostaAsync(usuario));
    }

    private async Task<UsuarioLogadoResponse> MontarRespostaAsync(Usuario usuario) =>
        new(usuario.Id, usuario.NomeCompleto, usuario.Email!, await userManager.GetRolesAsync(usuario));
}
