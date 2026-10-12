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
    LocaisDasLojas locais, ColetorClima coletor, CobrancasService cobrancas)
    : ControllerBase
{
    // GET /api/plataforma/empresas → todos os clientes com números de USO (nada de vendas ou faturamento deles)
    [HttpGet]
    public async Task<List<EmpresaResumoResponse>> Listar()
    {
        var empresas = await Clientes().AsNoTracking().Include(e => e.Plano).OrderBy(e => e.Id).ToListAsync();
        return await ResumirAsync(empresas);
    }

    // GET /api/plataforma/visao-geral → o painel do dono (por enquanto: clientes e uso; faturamento vem com as cobranças)
    [HttpGet("~/api/plataforma/visao-geral")]
    public async Task<VisaoGeralResponse> VisaoGeral()
    {
        var empresas = await Clientes().AsNoTracking().Include(e => e.Plano).OrderBy(e => e.Id).ToListAsync();
        var resumos = await ResumirAsync(empresas);
        var agora = DateTimeOffset.UtcNow;
        var reais = resumos.Where(r => !r.Demonstracao).ToList();
        return new VisaoGeralResponse(
            reais.Count, reais.Count(r => r.Ativa), reais.Count(r => !r.Ativa), resumos.Count(r => r.Demonstracao),
            reais.Where(r => r.Ativa).Sum(r => r.LimiteCaixas), reais.Where(r => r.Ativa).Sum(r => r.UsuariosAtivos),
            reais.Count(r => r.UltimoAcesso > agora.AddDays(-1)), reais.Count(r => r.UltimoAcesso > agora.AddDays(-7)),
            reais.Where(r => r.Ativa && (r.UltimoAcesso is null || r.UltimoAcesso < agora.AddDays(-7)))
                .OrderBy(r => r.UltimoAcesso ?? DateTimeOffset.MinValue).ToList(),
            reais.OrderByDescending(r => r.CriadoEm).Take(5).ToList(),
            await FaturamentoAsync(empresas));
    }

    // O faturamento do DONO (mensalidades): receita mensal da carteira, recebido no mês, a receber, atrasado e 12 meses.
    private async Task<FaturamentoPlataforma> FaturamentoAsync(List<Empresa> empresas)
    {
        var hoje = Relogio.Hoje;
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var desde = inicioMes.AddMonths(-11);
        var pagantes = empresas.Where(e => e.Ativa && CobrancasService.Cobra(e)).ToList();
        var faturas = await db.Faturas.AsNoTracking().Where(f => f.Status != StatusFatura.Cancelada
                && (f.Competencia >= desde || (f.PagaEm != null && f.PagaEm >= desde) || f.Status == StatusFatura.Aberta))
            .Select(f => new { f.ClienteId, f.Competencia, f.Vencimento, f.Valor, f.Status, f.PagaEm, f.ValorPago }).ToListAsync();
        var abertas = faturas.Where(f => f.Status == StatusFatura.Aberta).ToList();
        var meses = Enumerable.Range(0, 12).Select(i => desde.AddMonths(i)).Select(m => new MesPlataforma(m,
            faturas.Where(f => f.Competencia == m).Sum(f => f.Valor),
            faturas.Where(f => f.PagaEm is { } p && p.Year == m.Year && p.Month == m.Month).Sum(f => f.ValorPago ?? 0))).ToList();
        var porPlano = pagantes.GroupBy(e => e.Plano?.Nome ?? "Sem plano")
            .Select(g => new PlanoNaCarteira(g.Key, g.Count(), g.Sum(e => CobrancasService.ValorMensal(e, e.Plano))))
            .OrderByDescending(p => p.Receita).ToList();
        return new FaturamentoPlataforma(
            pagantes.Sum(e => CobrancasService.ValorMensal(e, e.Plano)), pagantes.Count,
            meses[^1].Recebido,
            abertas.Where(f => f.Vencimento >= hoje).Sum(f => f.Valor),
            abertas.Where(f => f.Vencimento < hoje).Sum(f => f.Valor),
            abertas.Where(f => f.Vencimento < hoje).Select(f => f.ClienteId).Distinct().Count(),
            empresas.Count(e => !e.Ativa && e.MotivoSuspensao == MotivosSuspensao.Atraso),
            meses, porPlano);
    }

    // PUT /api/plataforma/empresas/5/assinatura → plano, vencimento, teste grátis, valor combinado, cortesia
    [HttpPut("{id:int}/assinatura")]
    public async Task<ActionResult<ClienteDetalheResponse>> SalvarAssinatura(int id, AssinaturaRequest r)
    {
        var e = await Clientes().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        if (r.PlanoId is { } planoId && !await db.Planos.AnyAsync(p => p.Id == planoId))
            return BadRequest(new { mensagem = "Plano não encontrado." });
        if (!r.Cortesia && r.PlanoId is null && r.PrecoCombinado is null)
            return BadRequest(new { mensagem = "Escolha um plano (ou marque cortesia)." });
        (e.PlanoId, e.DiaVencimento, e.TesteAte, e.CobrarDesde, e.PrecoCombinado, e.Cortesia) =
            (r.PlanoId, r.DiaVencimento, r.TesteAte, r.CobrarDesde, r.PrecoCombinado is > 0 ? Math.Round(r.PrecoCombinado.Value, 2) : null, r.Cortesia);
        await db.SaveChangesAsync();
        await cobrancas.GerarAsync(Relogio.Hoje, id); // se já é hora, a mensalidade aparece agora
        return await DetalharAsync(e);
    }

    // GET /api/plataforma/empresas/5 → ficha + configurações da loja
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClienteDetalheResponse>> Detalhe(int id)
    {
        var e = await Clientes().AsNoTracking().Include(x => x.Plano).FirstOrDefaultAsync(x => x.Id == id);
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
        var empresa = await Clientes().Include(e => e.Plano).FirstOrDefaultAsync(e => e.Id == id);
        if (empresa is null) return NotFound();
        if (ativa) await cobrancas.ReativarAsync(empresa);
        else await cobrancas.SuspenderAsync(empresa, MotivosSuspensao.Manual);
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

    private async Task<ClienteDetalheResponse> DetalharAsync(Empresa e)
    {
        e.Plano ??= e.PlanoId is null ? null : await db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == e.PlanoId);
        var resumo = (await ResumirAsync([e]))[0];
        var hoje = Relogio.Hoje;
        var abertas = await db.Faturas.AsNoTracking().Where(f => f.ClienteId == e.Id && f.Status == StatusFatura.Aberta)
            .Select(f => new { f.Valor, f.Vencimento }).ToListAsync();
        var assinatura = new AssinaturaResponse(e.PlanoId, e.Plano?.Nome, e.DiaVencimento, e.TesteAte, e.CobrarDesde, e.PrecoCombinado,
            e.Cortesia, CobrancasService.ValorMensal(e, e.Plano), e.MotivoSuspensao, abertas.Count, abertas.Sum(f => f.Valor),
            abertas.Select(f => f.Vencimento < hoje ? hoje.DayNumber - f.Vencimento.DayNumber : 0).DefaultIfEmpty(0).Max());
        return new(resumo,
            new FichaCliente(e.RazaoSocial, e.Documento, e.Responsavel, e.Telefone, e.EmailContato, e.Endereco, e.Observacoes),
            e.Uf, e.Fuso, e.Horario, Services.Expediente.Descricao(e.Horario), assinatura);
    }

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
        var hoje = Relogio.Hoje;
        var atrasadas = await db.Faturas.Where(f => ids.Contains(f.ClienteId) && f.Status == StatusFatura.Aberta && f.Vencimento < hoje)
            .GroupBy(f => f.ClienteId).Select(g => new { g.Key, Total = g.Count(), Mais = g.Min(f => f.Vencimento) })
            .ToDictionaryAsync(x => x.Key);

        return empresas.Select(e =>
        {
            var u = usuariosPorEmpresa.GetValueOrDefault(e.Id);
            var m = movimento.GetValueOrDefault(e.Id);
            return new EmpresaResumoResponse(e.Id, e.Nome, e.Subdominio, e.LimiteCaixas, e.Ativa, e.Demonstracao, e.CriadoEm,
                u?.Total ?? 0, u?.Ativos ?? 0, produtos.GetValueOrDefault(e.Id), m?.Dias ?? 0,
                u?.UltimoAcesso, m?.Ultimo, caixas.GetValueOrDefault(e.Id), e.Cidade is null ? null : $"{e.Cidade}/{e.Uf}",
                e.VendasAutomaticas, e.Responsavel, e.Telefone, e.Plano?.Nome, CobrancasService.ValorMensal(e, e.Plano), e.Cortesia,
                e.MotivoSuspensao, atrasadas.GetValueOrDefault(e.Id)?.Total ?? 0,
                atrasadas.GetValueOrDefault(e.Id) is { } a ? hoje.DayNumber - a.Mais.DayNumber : 0);
        }).ToList();
    }
}
