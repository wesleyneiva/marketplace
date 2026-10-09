using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Administração do SaaS: os CLIENTES (empresas). Só o dono da plataforma, só pela rede interna.
// Aqui as consultas atravessam empresas de propósito: IgnoreQueryFilters() desliga o filtro de empresa
// (em nenhum outro lugar do sistema isso é usado para dados de clientes).
[ApiController]
[Route("api/plataforma/empresas")]
[DonoDaPlataforma]
public class PlataformaController(AppDbContext db, UserManager<Usuario> usuarios, IConfiguration config, IServiceProvider services)
    : ControllerBase
{
    // GET /api/plataforma/empresas → todos os clientes com números de uso
    [HttpGet]
    public async Task<List<EmpresaResumoResponse>> Listar()
    {
        var empresas = await db.Empresas.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        return await ResumirAsync(empresas);
    }

    // POST /api/plataforma/empresas → cliente novo (empresa + categorias + administrador com senha provisória)
    [HttpPost]
    public async Task<ActionResult<EmpresaCriadaResponse>> Criar(NovaEmpresaRequest r)
    {
        var apelido = string.IsNullOrWhiteSpace(r.Subdominio) ? NovaEmpresa.Apelido(r.Nome) : r.Subdominio.Trim().ToLowerInvariant();
        try
        {
            var (id, senha) = await NovaEmpresa.CriarAsync(services, r.Nome, apelido, r.LimiteCaixas, r.AdminEmail, r.AdminNome);
            var empresa = await db.Empresas.AsNoTracking().FirstAsync(e => e.Id == id);
            return new EmpresaCriadaResponse((await ResumirAsync([empresa]))[0], r.AdminEmail.Trim(), senha);
        }
        catch (NovaEmpresa.NovaEmpresaException e)
        {
            return BadRequest(new { mensagem = e.Message });
        }
    }

    // PUT /api/plataforma/empresas/5 → nome e plano (quantidade de caixas)
    [HttpPut("{id:int}")]
    public async Task<ActionResult<EmpresaResumoResponse>> Editar(int id, EditarEmpresaRequest r)
    {
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == id);
        if (empresa is null) return NotFound();
        empresa.Nome = r.Nome.Trim();
        empresa.LimiteCaixas = r.LimiteCaixas;
        await db.SaveChangesAsync();
        return (await ResumirAsync([empresa]))[0];
    }

    // POST /api/plataforma/empresas/5/suspender → ninguém da empresa entra (quem está logado cai em até 1 min)
    [HttpPost("{id:int}/suspender")]
    public Task<ActionResult<EmpresaResumoResponse>> Suspender(int id) => MudarSituacaoAsync(id, ativa: false);

    // POST /api/plataforma/empresas/5/reativar
    [HttpPost("{id:int}/reativar")]
    public Task<ActionResult<EmpresaResumoResponse>> Reativar(int id) => MudarSituacaoAsync(id, ativa: true);

    // GET /api/plataforma/empresas/5/usuarios
    [HttpGet("{id:int}/usuarios")]
    public async Task<ActionResult<List<UsuarioDaEmpresaResponse>>> Usuarios(int id)
    {
        if (!await db.Empresas.AnyAsync(e => e.Id == id)) return NotFound();
        var lista = await db.Users.IgnoreQueryFilters().Where(u => u.EmpresaId == id).OrderBy(u => u.NomeCompleto).ToListAsync();
        var resposta = new List<UsuarioDaEmpresaResponse>();
        foreach (var u in lista)
            resposta.Add(new(u.Id, u.NomeCompleto, u.Email!, await usuarios.GetRolesAsync(u), u.Ativo, u.TrocarSenha,
                u.LockoutEnd > DateTimeOffset.UtcNow, u.UltimoAcessoEm));
        return resposta;
    }

    // POST /api/plataforma/empresas/5/usuarios/{usuarioId}/redefinir-senha
    // O "esqueci minha senha" por enquanto: o cliente liga, você gera uma senha provisória e passa para ele.
    [HttpPost("{id:int}/usuarios/{usuarioId}/redefinir-senha")]
    public async Task<ActionResult<SenhaRedefinidaResponse>> RedefinirSenha(int id, string usuarioId)
    {
        // Gravar usuário de OUTRA empresa só "de dentro" dela: um escopo com a empresa dele (o carimbo de empresa
        // do AppDbContext recusa gravar linha de outra empresa — inclusive para o dono da plataforma).
        using var escopo = services.CriarEscopo(id);
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = await escopo.ServiceProvider.GetRequiredService<AppDbContext>().Users.FirstOrDefaultAsync(u => u.Id == usuarioId);
        if (usuario is null) return NotFound();
        if (usuario.SomenteLeitura) return BadRequest(new { mensagem = "O visitante da demonstração não tem senha." });

        var senha = UsuariosController.GerarSenhaProvisoria();
        var token = await usuarios.GeneratePasswordResetTokenAsync(usuario);
        var resultado = await usuarios.ResetPasswordAsync(usuario, token, senha);
        if (!resultado.Succeeded)
            return BadRequest(new { mensagem = string.Join(" ", resultado.Errors.Select(AuthController.TraduzirErro).Distinct()) });
        usuario.TrocarSenha = true;
        await usuarios.SetLockoutEndDateAsync(usuario, null);
        await usuarios.ResetAccessFailedCountAsync(usuario);
        await usuarios.UpdateAsync(usuario);
        return new SenhaRedefinidaResponse(usuario.Email!, senha);
    }

    // ------------------------------------------------------------------ apoio

    private async Task<ActionResult<EmpresaResumoResponse>> MudarSituacaoAsync(int id, bool ativa)
    {
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == id);
        if (empresa is null) return NotFound();
        if (!ativa && id == Plataforma.EmpresaDona(config))
            return BadRequest(new { mensagem = "Esta é a empresa da plataforma (a sua): ela não pode ser suspensa." });

        empresa.Ativa = ativa;
        await db.SaveChangesAsync();
        if (!ativa)
        {
            // Troca o "carimbo de segurança" de todos: os cookies de quem está logado deixam de valer (cai em até 1 min).
            using var escopo = services.CriarEscopo(id);
            var daEmpresa = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            foreach (var u in await escopo.ServiceProvider.GetRequiredService<AppDbContext>().Users.ToListAsync())
                await daEmpresa.UpdateSecurityStampAsync(u);
        }
        return (await ResumirAsync([empresa]))[0];
    }

    private async Task<List<EmpresaResumoResponse>> ResumirAsync(List<Empresa> empresas)
    {
        var ids = empresas.Select(e => e.Id).ToList();
        var desde = DateTimeOffset.UtcNow.AddDays(-30);

        var usuariosPorEmpresa = await db.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.EmpresaId) && !u.SomenteLeitura)
            .GroupBy(u => u.EmpresaId)
            .Select(g => new { g.Key, Total = g.Count(), Ativos = g.Count(u => u.Ativo), UltimoAcesso = g.Max(u => u.UltimoAcessoEm) })
            .ToDictionaryAsync(x => x.Key);
        var produtos = await db.Produtos.IgnoreQueryFilters().Where(p => ids.Contains(p.EmpresaId) && p.Ativo)
            .GroupBy(p => p.EmpresaId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total);
        var vendas = await db.Vendas.IgnoreQueryFilters()
            .Where(v => ids.Contains(v.EmpresaId) && v.Status == StatusVenda.Concluida && v.Origem != OrigemVenda.Historico)
            .GroupBy(v => v.EmpresaId)
            .Select(g => new
            {
                g.Key,
                Quantidade = g.Count(v => v.DataHora >= desde),
                Total = g.Where(v => v.DataHora >= desde).Sum(v => (decimal?)v.Total) ?? 0,
                Ultima = g.Max(v => (DateTimeOffset?)v.DataHora),
            })
            .ToDictionaryAsync(x => x.Key);
        var caixas = await db.SessoesCaixa.IgnoreQueryFilters().Where(s => ids.Contains(s.EmpresaId) && s.Status == StatusSessao.Aberta)
            .GroupBy(s => s.EmpresaId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total);

        return empresas.Select(e =>
        {
            var u = usuariosPorEmpresa.GetValueOrDefault(e.Id);
            var v = vendas.GetValueOrDefault(e.Id);
            return new EmpresaResumoResponse(e.Id, e.Nome, e.Subdominio, e.LimiteCaixas, e.Ativa, e.Demonstracao, e.CriadoEm,
                u?.Total ?? 0, u?.Ativos ?? 0, produtos.GetValueOrDefault(e.Id), v?.Quantidade ?? 0, v?.Total ?? 0,
                u?.UltimoAcesso, v?.Ultima, caixas.GetValueOrDefault(e.Id));
        }).ToList();
    }
}
