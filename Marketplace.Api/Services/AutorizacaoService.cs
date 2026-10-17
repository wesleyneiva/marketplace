using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services.Simulador;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

public class AutorizacaoException(string mensagem) : Exception(mensagem);

// Autorização do gerente no caixa. O operador (perfil Caixa) não cancela nada sozinho: o gerente escolhe o nome
// dele na lista e digita a PRÓPRIA senha ali no caixa. Se quem está logado já é gerente/administrador, ele mesmo
// autoriza (sem senha de novo). Tudo fica registrado em AutorizacoesGerente.
public class AutorizacaoService(AppDbContext db, UserManager<Usuario> usuarios, SignInManager<Usuario> login)
{
    // Os gerentes e administradores da loja (para a lista do caixa). Sem o robô do simulador e sem o visitante.
    public async Task<List<GerenteResponse>> GerentesAsync()
    {
        var ids = (await usuarios.GetUsersInRoleAsync(Perfis.Gerente)).Concat(await usuarios.GetUsersInRoleAsync(Perfis.Administrador))
            .Where(u => u.EmpresaId == db.EmpresaAtual && u.Ativo && !u.SomenteLeitura && !SimuladorEstado.EhRobo(u.Email)
                        && !(u.Email ?? "").EndsWith("@sistema.local"))
            .DistinctBy(u => u.Id)
            .Select(u => new GerenteResponse(u.Id, u.NomeCompleto))
            .ToList();
        return ids.OrderBy(g => g.Nome, StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), true)).ToList();
    }

    // Confere quem autoriza e devolve o Id dele. Senha errada conta para o bloqueio da conta (5 erros = 15 min).
    public async Task<string> ConferirAsync(string operadorId, bool operadorEhGerencia, AutorizacaoRequest? autorizacao)
    {
        if (operadorEhGerencia && (autorizacao is null || autorizacao.GerenteId == operadorId)) return operadorId;
        if (autorizacao is null || string.IsNullOrWhiteSpace(autorizacao.GerenteId))
            throw new AutorizacaoException("Precisa da autorização de um gerente: escolha o gerente e peça a senha dele.");
        if (string.IsNullOrEmpty(autorizacao.Senha))
            throw new AutorizacaoException("O gerente precisa digitar a senha.");

        var gerente = await usuarios.FindByIdAsync(autorizacao.GerenteId);
        if (gerente is null || gerente.EmpresaId != db.EmpresaAtual || !gerente.Ativo || gerente.SomenteLeitura)
            throw new AutorizacaoException("Gerente não encontrado.");
        if (!await usuarios.IsInRoleAsync(gerente, Perfis.Gerente) && !await usuarios.IsInRoleAsync(gerente, Perfis.Administrador))
            throw new AutorizacaoException($"{gerente.NomeCompleto} não é gerente.");

        var r = await login.CheckPasswordSignInAsync(gerente, autorizacao.Senha, lockoutOnFailure: true);
        if (r.IsLockedOut) throw new AutorizacaoException($"Muitas senhas erradas: a conta de {gerente.NomeCompleto} ficou bloqueada por 15 minutos.");
        if (!r.Succeeded) throw new AutorizacaoException("Senha do gerente errada.");
        return gerente.Id;
    }

    // Prepara o registro (quem chama grava, junto com o resto, num SaveChanges só).
    public AutorizacaoGerente Registrar(AcaoAutorizada acao, string operadorId, string autorizadoPorId, int? sessaoCaixaId,
        string motivo, string descricao, decimal valor, Venda? venda = null)
    {
        var a = new AutorizacaoGerente
        {
            Acao = acao, OperadorId = operadorId, AutorizadoPorId = autorizadoPorId, SessaoCaixaId = sessaoCaixaId,
            Motivo = Cortar(motivo.Trim()), Descricao = Cortar(descricao), Valor = valor, Venda = venda,
        };
        db.AutorizacoesGerente.Add(a);
        return a;
    }

    private static string Cortar(string t) => t.Length > 200 ? t[..200] : t;
}
