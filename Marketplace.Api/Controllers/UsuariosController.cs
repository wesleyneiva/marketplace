using System.Security.Claims;
using System.Security.Cryptography;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services.Simulador;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Cadastro de funcionários: só o Administrador.
// Usuário nunca é apagado (vendas e movimentações antigas apontam para ele): é DESATIVADO.
[ApiController]
[Route("api/usuarios")]
[Authorize(Roles = Perfis.Administrador)]
public class UsuariosController(UserManager<Usuario> usuarios, AppDbContext db) : ControllerBase
{
    private string MeuId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET /api/usuarios
    [HttpGet]
    public async Task<List<UsuarioResponse>> Listar()
    {
        var todos = await usuarios.Users.AsNoTracking().OrderBy(u => u.NomeCompleto).ToListAsync();
        var lista = new List<UsuarioResponse>();
        foreach (var u in todos)
            lista.Add(await ParaResposta(u));
        // Ativos primeiro; o "Caixa Automático" (sistema) por último.
        return lista.OrderBy(u => u.Sistema).ThenByDescending(u => u.Ativo).ThenBy(u => u.Nome).ToList();
    }

    // POST /api/usuarios → cria com senha provisória (mostrada uma vez)
    [HttpPost]
    public async Task<IActionResult> Criar(NovoUsuarioRequest r)
    {
        if (!Perfis.Todos.Contains(r.Perfil))
            return BadRequest(new { mensagem = "Perfil inválido." });

        // O e-mail é o login: precisa ser único no sistema INTEIRO (não só nesta empresa) — senão, no login,
        // não daria para saber de qual empresa é a pessoa. (Sem dizer de qual empresa ele é, claro.)
        var normalizado = usuarios.NormalizeEmail(r.Email.Trim());
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalizado))
            return BadRequest(new { mensagem = "Já existe um usuário com este e-mail." });

        var senha = GerarSenhaProvisoria();
        var usuario = new Usuario
        {
            UserName = r.Email.Trim(), Email = r.Email.Trim(), EmailConfirmed = true,
            NomeCompleto = r.Nome.Trim(), TrocarSenha = true,
        };
        var criado = await usuarios.CreateAsync(usuario, senha);
        if (!criado.Succeeded)
            return BadRequest(new { mensagem = string.Join(" ", criado.Errors.Select(AuthController.TraduzirErro).Distinct()) });

        await usuarios.AddToRoleAsync(usuario, r.Perfil);
        return Ok(new SenhaProvisoriaResponse(await ParaResposta(usuario), senha));
    }

    // PUT /api/usuarios/{id} → nome e perfil
    [HttpPut("{id}")]
    public async Task<IActionResult> Editar(string id, EditarUsuarioRequest r)
    {
        if (await CarregarAsync(id) is not { } usuario) return NotFound();
        if (!Perfis.Todos.Contains(r.Perfil)) return BadRequest(new { mensagem = "Perfil inválido." });

        var perfilAtual = (await usuarios.GetRolesAsync(usuario)).FirstOrDefault();
        if (perfilAtual != r.Perfil)
        {
            if (id == MeuId)
                return BadRequest(new { mensagem = "Você não pode mudar o seu próprio perfil (peça a outro administrador)." });
            if (perfilAtual == Perfis.Administrador && await AdminsAtivosAsync() <= 1)
                return BadRequest(new { mensagem = "Este é o último administrador ativo: o sistema não pode ficar sem nenhum." });

            if (perfilAtual is not null) await usuarios.RemoveFromRoleAsync(usuario, perfilAtual);
            await usuarios.AddToRoleAsync(usuario, r.Perfil);
            await usuarios.UpdateSecurityStampAsync(usuario); // as permissões novas valem já (o cookie antigo cai)
        }

        usuario.NomeCompleto = r.Nome.Trim();
        await usuarios.UpdateAsync(usuario);
        return Ok(await ParaResposta(usuario));
    }

    // POST /api/usuarios/{id}/desativar
    [HttpPost("{id}/desativar")]
    public async Task<IActionResult> Desativar(string id)
    {
        if (await CarregarAsync(id) is not { } usuario) return NotFound();
        if (id == MeuId)
            return BadRequest(new { mensagem = "Você não pode desativar a si mesmo." });
        if (await usuarios.IsInRoleAsync(usuario, Perfis.Administrador) && await AdminsAtivosAsync() <= 1)
            return BadRequest(new { mensagem = "Este é o último administrador ativo: o sistema não pode ficar sem nenhum." });

        usuario.Ativo = false;
        await usuarios.UpdateAsync(usuario);
        await usuarios.UpdateSecurityStampAsync(usuario); // derruba quem estiver logado com ele (em até 1 min)
        return Ok(await ParaResposta(usuario));
    }

    // POST /api/usuarios/{id}/reativar
    [HttpPost("{id}/reativar")]
    public async Task<IActionResult> Reativar(string id)
    {
        if (await CarregarAsync(id) is not { } usuario) return NotFound();
        usuario.Ativo = true;
        await usuarios.UpdateAsync(usuario);
        return Ok(await ParaResposta(usuario));
    }

    // POST /api/usuarios/{id}/redefinir-senha → nova senha provisória (esqueceu a senha)
    [HttpPost("{id}/redefinir-senha")]
    public async Task<IActionResult> RedefinirSenha(string id)
    {
        if (await CarregarAsync(id) is not { } usuario) return NotFound();
        if (id == MeuId)
            return BadRequest(new { mensagem = "Para a sua própria senha, use \"Trocar minha senha\"." });

        var senha = GerarSenhaProvisoria();
        var token = await usuarios.GeneratePasswordResetTokenAsync(usuario);
        var resultado = await usuarios.ResetPasswordAsync(usuario, token, senha);
        if (!resultado.Succeeded)
            return BadRequest(new { mensagem = string.Join(" ", resultado.Errors.Select(AuthController.TraduzirErro).Distinct()) });

        usuario.TrocarSenha = true;
        await usuarios.SetLockoutEndDateAsync(usuario, null); // se estava bloqueado por errar a senha, libera
        await usuarios.ResetAccessFailedCountAsync(usuario);
        await usuarios.UpdateAsync(usuario);
        return Ok(new SenhaProvisoriaResponse(await ParaResposta(usuario), senha));
    }

    // ---------------------------------------------------------------- apoio

    // Usuários "do sistema" (o robô do simulador e o visitante da demonstração) não podem ser mexidos por aqui.
    private static bool DoSistema(Usuario u) => u.Email == SimuladorEstado.Email || u.SomenteLeitura;

    private async Task<Usuario?> CarregarAsync(string id)
    {
        var u = await usuarios.FindByIdAsync(id);
        return u is null || DoSistema(u) ? null : u;
    }

    private async Task<int> AdminsAtivosAsync() =>
        (await usuarios.GetUsersInRoleAsync(Perfis.Administrador)).Count(u => u.Ativo);

    private async Task<UsuarioResponse> ParaResposta(Usuario u) => new(
        u.Id, u.NomeCompleto, u.Email!, (await usuarios.GetRolesAsync(u)).FirstOrDefault() ?? "—",
        u.Ativo, u.TrocarSenha, DoSistema(u), u.CriadoEm, u.UltimoAcessoEm);

    // Senha provisória legível e dentro das regras: ex. "Caju-4827-Mesa".
    private static readonly string[] Palavras =
        ["Arroz", "Feijao", "Caju", "Mesa", "Pera", "Milho", "Cesta", "Balcao", "Banana", "Limao", "Uva", "Trigo"];

    public static string GerarSenhaProvisoria() =>
        $"{Palavras[RandomNumberGenerator.GetInt32(Palavras.Length)]}-{RandomNumberGenerator.GetInt32(1000, 10000)}-" +
        $"{Palavras[RandomNumberGenerator.GetInt32(Palavras.Length)]}";
}
