using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Login com cookie: a API devolve um cookie "HttpOnly" (o JavaScript não consegue ler,
// o navegador só o envia de volta). Assim não guardamos token nenhum no navegador.
[ApiController]
[Route("api/auth")]
public class AuthController(
    SignInManager<Usuario> signInManager, UserManager<Usuario> userManager, AppDbContext db, IConfiguration config) : ControllerBase
{
    // Pela internet, login com senha SÓ no endereço dos clientes (app.wnlabs.com.br). Na vitrine
    // (demo.wnlabs.com.br) só existe o botão da demonstração. Pelo Tailscale, tudo liberado.
    // Para testar em casa: Publico__HostClientes=oracle-a1 (e um cabeçalho CF-Connecting-IP de mentira).
    private bool LoginComSenhaLiberado => !HttpContext.VeioDaInternet() || EnderecoDosClientes;
    private bool EnderecoDosClientes =>
        string.Equals(Request.Host.Host, config["Publico:HostClientes"] ?? "app.wnlabs.com.br", StringComparison.OrdinalIgnoreCase);
    // O botão "Ver demonstração" só existe no endereço da vitrine (demo.wnlabs.com.br). Pelo Tailscale
    // (oracle-a1:5100) e nos endereços dos clientes, o login é o normal. Para testar em casa:
    // Demonstracao__Host=oracle-a1.
    private bool DemonstracaoLigada =>
        config.GetValue("Demonstracao:Ativa", false)
        && string.Equals(Request.Host.Host, config["Demonstracao:Host"] ?? "demo.wnlabs.com.br", StringComparison.OrdinalIgnoreCase);

    // GET /api/auth/acesso → a tela de login pergunta o que mostrar
    [HttpGet("acesso")]
    public AcessoInfoResponse Acesso() => new(DemonstracaoLigada, LoginComSenhaLiberado);

    // POST /api/auth/login   { "email": "...", "senha": "..." }
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (!LoginComSenhaLiberado)
            return StatusCode(StatusCodes.Status403Forbidden, new { mensagem = "Por este endereço, use o botão \"Ver demonstração\"." });

        var usuario = await userManager.FindByEmailAsync(request.Email);
        if (usuario is null)
            return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });

        // Primeiro só CONFERE a senha (sem logar). lockoutOnFailure: 5 senhas erradas → bloqueio de 15 minutos.
        var resultado = await signInManager.CheckPasswordSignInAsync(usuario, request.Senha, lockoutOnFailure: true);

        if (usuario.SomenteLeitura) // visitante não tem senha: entra só pelo botão da demonstração
            return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });

        if (resultado.IsLockedOut)
            return Unauthorized(new { mensagem = "Muitas tentativas erradas. Conta bloqueada por 15 minutos." });

        if (!resultado.Succeeded)
            return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });

        // Usuários da empresa de demonstração (o seu administrador, por exemplo) não entram pela internet,
        // nem com a senha certa: o acesso de dono do sistema continua só pelo Tailscale.
        if (HttpContext.VeioDaInternet() && await db.Empresas.AnyAsync(e => e.Id == usuario.EmpresaId && e.Demonstracao))
            return StatusCode(StatusCodes.Status403Forbidden, new { mensagem = "Este usuário só entra pela rede interna." });

        // Só depois da senha certa dizemos que a conta está desativada (senão qualquer um descobriria quem existe).
        if (!usuario.Ativo)
            return StatusCode(StatusCodes.Status403Forbidden, new { mensagem = "Usuário desativado. Fale com o administrador." });

        if (!await EmpresaAtivaAsync(usuario.EmpresaId))
            return StatusCode(StatusCodes.Status403Forbidden, new { mensagem = "O acesso desta empresa está suspenso." });

        await signInManager.SignInAsync(usuario, isPersistent: false);
        usuario.UltimoAcessoEm = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(usuario);

        return Ok(await MontarRespostaAsync(usuario));
    }

    // POST /api/auth/demonstracao → entra como VISITANTE (só olha; não grava nada). Sem senha.
    [HttpPost("demonstracao")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Demonstracao()
    {
        if (!DemonstracaoLigada) return NotFound();
        var visitante = await userManager.FindByEmailAsync(config["Demonstracao:Email"] ?? SeedDemonstracao.EmailPadrao);
        if (visitante is null || !visitante.SomenteLeitura || !visitante.Ativo) return NotFound();

        await signInManager.SignInAsync(visitante, isPersistent: false);
        return Ok(await MontarRespostaAsync(visitante));
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

    private async Task<UsuarioLogadoResponse> MontarRespostaAsync(Usuario usuario)
    {
        var empresa = await db.Empresas.AsNoTracking().Where(e => e.Id == usuario.EmpresaId)
            .Select(e => new EmpresaResponse(e.Id, e.Nome, e.LimiteCaixas, e.Demonstracao)).FirstAsync();
        var perfis = await userManager.GetRolesAsync(usuario);
        var dono = !usuario.SomenteLeitura && perfis.Contains(Perfis.Administrador)
                   && usuario.EmpresaId == Plataforma.EmpresaDona(config) && !HttpContext.VeioDaInternet();
        return new(usuario.Id, usuario.NomeCompleto, usuario.Email!, perfis, usuario.TrocarSenha, empresa, usuario.SomenteLeitura, dono);
    }

    private Task<bool> EmpresaAtivaAsync(int empresaId) => db.Empresas.AnyAsync(e => e.Id == empresaId && e.Ativa);
}
