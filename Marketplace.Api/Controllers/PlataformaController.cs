using Marketplace.Api.Services;
using Marketplace.Api.Services.Simulador;
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
public class PlataformaController(AppDbContext db, UserManager<Usuario> usuarios, IConfiguration config, IServiceProvider services,
    LocaisDasLojas locais, ColetorClima coletor)
    : ControllerBase
{
    // GET /api/plataforma/empresas → todos os clientes com números de USO (nada de vendas ou faturamento deles)
    [HttpGet]
    public async Task<List<EmpresaResumoResponse>> Listar()
    {
        var empresas = await Clientes().AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        return await ResumirAsync(empresas);
    }

    // GET /api/plataforma/visao-geral → o painel do dono (por enquanto: clientes e uso; faturamento vem com as cobranças)
    [HttpGet("~/api/plataforma/visao-geral")]
    public async Task<VisaoGeralResponse> VisaoGeral()
    {
        var resumos = await ResumirAsync(await Clientes().AsNoTracking().OrderBy(e => e.Id).ToListAsync());
        var agora = DateTimeOffset.UtcNow;
        var reais = resumos.Where(r => !r.Demonstracao).ToList();
        return new VisaoGeralResponse(
            reais.Count, reais.Count(r => r.Ativa), reais.Count(r => !r.Ativa), resumos.Count(r => r.Demonstracao),
            reais.Where(r => r.Ativa).Sum(r => r.LimiteCaixas), reais.Where(r => r.Ativa).Sum(r => r.UsuariosAtivos),
            reais.Count(r => r.UltimoAcesso > agora.AddDays(-1)), reais.Count(r => r.UltimoAcesso > agora.AddDays(-7)),
            reais.Where(r => r.Ativa && (r.UltimoAcesso is null || r.UltimoAcesso < agora.AddDays(-7)))
                .OrderBy(r => r.UltimoAcesso ?? DateTimeOffset.MinValue).ToList(),
            reais.OrderByDescending(r => r.CriadoEm).Take(5).ToList());
    }

    // GET /api/plataforma/empresas/5 → ficha + configurações da loja
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClienteDetalheResponse>> Detalhe(int id)
    {
        var e = await Clientes().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return e is null ? NotFound() : await DetalharAsync(e);
    }

    // PUT /api/plataforma/empresas/5/cidade → cidade da loja (clima + fuso), como em Configurações do cliente
    [HttpPut("{id:int}/cidade")]
    public async Task<ActionResult<ClienteDetalheResponse>> SalvarCidade(int id, CidadeRequest r)
    {
        if (!Services.Geografia.FusoValido(r.Fuso)) return BadRequest(new { mensagem = "Fuso horário desconhecido." });
        var e = await Clientes().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        (e.Cidade, e.Uf, e.Latitude, e.Longitude, e.Fuso) =
            (r.Nome.Trim(), r.Uf.Trim().ToUpperInvariant(), Math.Round(r.Latitude, 5), Math.Round(r.Longitude, 5), r.Fuso);
        await db.SaveChangesAsync();
        locais.Esquecer(id);
        coletor.Acordar();
        return await DetalharAsync(e);
    }

    // PUT /api/plataforma/empresas/5/horario → horário de funcionamento, como em Configurações do cliente
    [HttpPut("{id:int}/horario")]
    public async Task<ActionResult<ClienteDetalheResponse>> SalvarHorario(int id, HorarioFuncionamento r)
    {
        if (EmpresaController.ValidarHorario(r) is { } erro) return BadRequest(new { mensagem = erro });
        var e = await Clientes().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        e.Horario = EmpresaController.Normalizar(r);
        await db.SaveChangesAsync();
        locais.Esquecer(id);
        return await DetalharAsync(e);
    }

    // POST /api/plataforma/empresas → cliente novo (empresa + categorias + administrador com senha provisória)
    [HttpPost]
    public async Task<ActionResult<EmpresaCriadaResponse>> Criar(NovaEmpresaRequest r)
    {
        var apelido = string.IsNullOrWhiteSpace(r.Subdominio) ? NovaEmpresa.Apelido(r.Nome) : r.Subdominio.Trim().ToLowerInvariant();
        if (r.Ficha is not null && ValidarFicha(r.Ficha) is { } erroFicha) return BadRequest(new { mensagem = erroFicha });
        try
        {
            var (id, senha) = await NovaEmpresa.CriarAsync(services, r.Nome, apelido, r.LimiteCaixas, r.AdminEmail, r.AdminNome, r.Cidade);
            var empresa = await db.Empresas.FirstAsync(e => e.Id == id);
            if (r.Ficha is not null)
            {
                AplicarFicha(empresa, r.Ficha);
                await db.SaveChangesAsync();
            }
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
        var empresa = await Clientes().FirstOrDefaultAsync(e => e.Id == id);
        if (empresa is null) return NotFound();
        if (r.Ficha is not null && ValidarFicha(r.Ficha) is { } erroFicha) return BadRequest(new { mensagem = erroFicha });
        if (r.VendasAutomaticas is not null && !ModosVendasAutomaticas.Valido(r.VendasAutomaticas))
            return BadRequest(new { mensagem = "Vendas automáticas: Desligado, SoVendas ou Completo." });
        if (r.VendasAutomaticas == ModosVendasAutomaticas.Completo && !empresa.Demonstracao)
            return BadRequest(new { mensagem = "O modo completo (compras, contas e fiado automáticos) é só da demonstração." });
        empresa.Nome = r.Nome.Trim();
        empresa.LimiteCaixas = r.LimiteCaixas;
        if (r.VendasAutomaticas is not null) empresa.VendasAutomaticas = r.VendasAutomaticas;
        if (r.Ficha is not null) AplicarFicha(empresa, r.Ficha);
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
        if (!await Clientes().AnyAsync(e => e.Id == id)) return NotFound();
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
        if (!await Clientes().AnyAsync(e => e.Id == id)) return NotFound();
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
        var empresa = await Clientes().FirstOrDefaultAsync(e => e.Id == id);
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

    // Os clientes: todas as empresas, menos a da própria plataforma (a do dono).
    private IQueryable<Empresa> Clientes() => db.Empresas.Where(e => !e.Plataforma);

    private static string? ValidarFicha(FichaCliente f)
    {
        var doc = Services.Documentos.SoDigitos(f.Documento);
        if (!Services.Documentos.Valido(doc)) return "CPF ou CNPJ inválido (confira os números ou deixe em branco).";
        var fone = Services.Documentos.SoDigitos(f.Telefone);
        if (fone.Length is > 0 and (< 10 or > 13)) return "Telefone: DDD + número (ex.: 51 99999-8888).";
        return null;
    }

    private static void AplicarFicha(Empresa e, FichaCliente f)
    {
        static string? Limpo(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        e.RazaoSocial = Limpo(f.RazaoSocial);
        e.Documento = Limpo(Services.Documentos.SoDigitos(f.Documento));
        e.Responsavel = Limpo(f.Responsavel);
        e.Telefone = Limpo(Services.Documentos.SoDigitos(f.Telefone));
        e.EmailContato = Limpo(f.EmailContato);
        e.Endereco = Limpo(f.Endereco);
        e.Observacoes = Limpo(f.Observacoes);
    }

    private async Task<ClienteDetalheResponse> DetalharAsync(Empresa e) => new(
        (await ResumirAsync([e]))[0],
        new FichaCliente(e.RazaoSocial, e.Documento, e.Responsavel, e.Telefone, e.EmailContato, e.Endereco, e.Observacoes),
        e.Uf, e.Fuso, e.Horario, Services.Expediente.Descricao(e.Horario));

    // Números de USO de cada cliente. De propósito, nada de valores de venda: o dono do sistema não precisa (nem deve)
    // saber quanto cada mercado fatura. "Dias com movimento" mostra se o sistema está sendo usado, sem dizer quanto.
    private async Task<List<EmpresaResumoResponse>> ResumirAsync(List<Empresa> empresas)
    {
        var ids = empresas.Select(e => e.Id).ToList();
        var desde = DateTimeOffset.UtcNow.AddDays(-30);

        var usuariosPorEmpresa = await db.Users.IgnoreQueryFilters()
            .Where(u => ids.Contains(u.EmpresaId) && !u.SomenteLeitura && u.Email != SimuladorEstado.Email && !u.Email!.EndsWith("@sistema.local"))
            .GroupBy(u => u.EmpresaId)
            .Select(g => new { g.Key, Total = g.Count(), Ativos = g.Count(u => u.Ativo), UltimoAcesso = g.Max(u => u.UltimoAcessoEm) })
            .ToDictionaryAsync(x => x.Key);
        var produtos = await db.Produtos.IgnoreQueryFilters().Where(p => ids.Contains(p.EmpresaId) && p.Ativo)
            .GroupBy(p => p.EmpresaId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total);
        var movimento = await db.Vendas.IgnoreQueryFilters()
            .Where(v => ids.Contains(v.EmpresaId) && v.Origem != OrigemVenda.Historico)
            .GroupBy(v => v.EmpresaId)
            .Select(g => new
            {
                g.Key,
                Dias = g.Where(v => v.DataHora >= desde).Select(v => v.DataHora.Date).Distinct().Count(),
                Ultimo = g.Max(v => (DateTimeOffset?)v.DataHora),
            })
            .ToDictionaryAsync(x => x.Key);
        var caixas = await db.SessoesCaixa.IgnoreQueryFilters().Where(s => ids.Contains(s.EmpresaId) && s.Status == StatusSessao.Aberta)
            .GroupBy(s => s.EmpresaId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total);

        return empresas.Select(e =>
        {
            var u = usuariosPorEmpresa.GetValueOrDefault(e.Id);
            var m = movimento.GetValueOrDefault(e.Id);
            return new EmpresaResumoResponse(e.Id, e.Nome, e.Subdominio, e.LimiteCaixas, e.Ativa, e.Demonstracao, e.CriadoEm,
                u?.Total ?? 0, u?.Ativos ?? 0, produtos.GetValueOrDefault(e.Id), m?.Dias ?? 0,
                u?.UltimoAcesso, m?.Ultimo, caixas.GetValueOrDefault(e.Id), e.Cidade is null ? null : $"{e.Cidade}/{e.Uf}",
                e.VendasAutomaticas, e.Responsavel, e.Telefone);
        }).ToList();
    }
}
