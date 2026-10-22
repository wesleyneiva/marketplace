using Marketplace.Api.Contracts;
using Marketplace.Api.Controllers;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Clientes da loja: cadastro (o mesmo do fiado), identificação no caixa e na oficina, e o HISTÓRICO (compras, trocas,
// ordens de serviço). O fiado continua com as regras dele (limite, caderneta); aqui é o "conhecer o cliente".
public class ClientesService(AppDbContext db)
{
    // ---------------------------------------------------------------- cadastro

    public async Task<string?> PreencherAsync(Cliente c, ClienteCadastroRequest r)
    {
        var cpf = SoDigitos(r.Cpf);
        if (cpf.Length == 0) return "Informe o CPF.";
        if (!FiadoController.CpfValido(cpf)) return "CPF inválido: confira os números.";
        if (await db.Clientes.AnyAsync(x => x.Cpf == cpf && x.Id != c.Id)) return "Já existe um cliente com este CPF.";
        var telefone = SoDigitos(r.Telefone);
        if (telefone.Length is > 0 and (< 10 or > 13)) return "Telefone: com DDD (ex.: 51 99999-8888).";
        if (r.Nascimento is { } n && (n > Relogio.Hoje || n.Year < 1900)) return "Data de nascimento inválida.";
        var email = Limpo(r.Email);
        if (email is not null && (!email.Contains('@') || !email.Contains('.'))) return "E-mail inválido.";
        c.Nome = r.Nome.Trim();
        c.Apelido = Limpo(r.Apelido);
        c.Telefone = telefone.Length > 0 ? telefone : null;
        c.Cpf = cpf.Length > 0 ? cpf : null;
        c.Email = email?.ToLowerInvariant();
        c.Endereco = Limpo(r.Endereco);
        c.Nascimento = r.Nascimento;
        c.Raquete = Limpo(r.Raquete);
        c.Corda = Limpo(r.Corda);
        c.Tensao = Limpo(r.Tensao);
        c.Observacao = Limpo(r.Observacao);
        return null;
    }

    // Cadastro rápido no caixa: se o telefone ou o CPF já é de alguém, devolve esse cliente (não duplica).
    public async Task<(Cliente? Cliente, string? Erro)> RapidoAsync(ClienteRapidoRequest r, string usuarioId)
    {
        var telefone = SoDigitos(r.Telefone);
        var cpf = SoDigitos(r.Cpf);
        if (telefone.Length is > 0 and (< 10 or > 13)) return (null, "Telefone: com DDD (ex.: 51 99999-8888).");
        if (cpf.Length == 0) return (null, "Informe o CPF.");
        if (!FiadoController.CpfValido(cpf)) return (null, "CPF inválido: confira os números.");
        var existente = await db.Clientes.FirstOrDefaultAsync(c =>
            (telefone.Length > 0 && c.Telefone == telefone) || (cpf.Length > 0 && c.Cpf == cpf));
        if (existente is not null)
        {
            existente.Ativo = true;
            await db.SaveChangesAsync();
            return (existente, null);
        }
        var cliente = new Cliente
        {
            Nome = r.Nome.Trim(), Telefone = telefone.Length > 0 ? telefone : null, Cpf = cpf.Length > 0 ? cpf : null, CriadoPorId = usuarioId,
        };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return (cliente, null);
    }

    // Oficina: a OS chega com nome e telefone. Acha o cliente pelo telefone (ou cria) e guarda o que ele usa
    // (raquete, corda, tensão), para a próxima vez. Sem telefone, não liga a ninguém.
    public async Task<int?> VincularDaOficinaAsync(string nome, string? telefone, string? raquete, string? corda, string? tensao,
        string? usuarioId, bool simulado = false)
    {
        var tel = SoDigitos(telefone);
        if (tel.Length is < 10 or > 13) return null;
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Telefone == tel);
        if (cliente is null)
        {
            cliente = new Cliente { Nome = nome.Trim(), Telefone = tel, CriadoPorId = usuarioId, Simulado = simulado };
            db.Clientes.Add(cliente);
        }
        if (!string.IsNullOrWhiteSpace(raquete)) cliente.Raquete = raquete.Trim();
        if (!string.IsNullOrWhiteSpace(corda) && !corda.Contains("do cliente", StringComparison.OrdinalIgnoreCase)) cliente.Corda = corda.Trim();
        if (!string.IsNullOrWhiteSpace(tensao)) cliente.Tensao = tensao.Trim();
        await db.SaveChangesAsync();
        return cliente.Id;
    }

    // ---------------------------------------------------------------- listas

    public static IQueryable<Cliente> Buscar(IQueryable<Cliente> q, string? busca)
    {
        if (string.IsNullOrWhiteSpace(busca)) return q;
        var termo = busca.Trim();
        var digitos = SoDigitos(termo);
        var like = $"%{termo}%";
        return q.Where(c => EF.Functions.ILike(EF.Functions.Unaccent(c.Nome), EF.Functions.Unaccent(like))
                            || (c.Apelido != null && EF.Functions.ILike(EF.Functions.Unaccent(c.Apelido), EF.Functions.Unaccent(like)))
                            || (c.Email != null && EF.Functions.ILike(c.Email, like))
                            || (digitos.Length >= 3 && ((c.Telefone != null && c.Telefone.Contains(digitos)) || (c.Cpf != null && c.Cpf.Contains(digitos)))));
    }

    // filtro: todos | aniversariantes (do mês) | sumidos (compraram, mas não há 60+ dias) | novos (30 dias) | inativos
    public async Task<ClientesPagina> ListarAsync(string? busca, string filtro, int pagina, int tamanho)
    {
        var hoje = Relogio.Hoje;
        var q = Buscar(db.Clientes.AsNoTracking(), busca);
        q = filtro == "inativos" ? q.Where(c => !c.Ativo) : q.Where(c => c.Ativo);
        var corte60 = Relogio.InicioDoDiaUtc(hoje.AddDays(-60));
        var corte30 = Relogio.InicioDoDiaUtc(hoje.AddDays(-30));
        var concluidas = db.Vendas.Where(v => v.Status == StatusVenda.Concluida);
        q = filtro switch
        {
            "aniversariantes" => q.Where(c => c.Nascimento != null && c.Nascimento.Value.Month == hoje.Month),
            "sumidos" => q.Where(c => concluidas.Any(v => v.ClienteId == c.Id) && !concluidas.Any(v => v.ClienteId == c.Id && v.DataHora >= corte60)),
            "novos" => q.Where(c => c.CriadoEm >= corte30),
            _ => q,
        };
        var total = await q.CountAsync();
        var linhas = q.Select(c => new
        {
            c.Id, c.Nome, c.Apelido, c.Telefone, c.Nascimento, c.Raquete, c.Ativo, c.Simulado,
            Compras = concluidas.Count(v => v.ClienteId == c.Id),
            Gasto = concluidas.Where(v => v.ClienteId == c.Id).Sum(v => (decimal?)v.Total) ?? 0,
            Ultima = concluidas.Where(v => v.ClienteId == c.Id).Max(v => (DateTimeOffset?)v.DataHora),
        });
        linhas = filtro switch
        {
            "aniversariantes" => linhas.OrderBy(c => c.Nascimento!.Value.Day),
            "sumidos" => linhas.OrderByDescending(c => c.Gasto),
            _ => linhas.OrderBy(c => c.Ultima == null).ThenByDescending(c => c.Ultima).ThenBy(c => c.Nome), // quem comprou por último primeiro
        };
        var itens = await linhas.Skip((pagina - 1) * tamanho).Take(tamanho).ToListAsync();

        var ativos = db.Clientes.Where(c => c.Ativo);
        return new ClientesPagina(
            itens.Select(c => new ClienteListaItem(c.Id, c.Nome, c.Apelido, c.Telefone, c.Nascimento, c.Raquete, c.Compras, c.Gasto, c.Ultima, c.Ativo, c.Simulado)).ToList(),
            total, pagina, tamanho, await ativos.CountAsync(),
            await ativos.CountAsync(c => c.Nascimento != null && c.Nascimento.Value.Month == hoje.Month),
            await ativos.CountAsync(c => concluidas.Any(v => v.ClienteId == c.Id && v.DataHora >= corte30)));
    }

    public async Task<List<ClienteBuscaPdv>> BuscarNoCaixaAsync(string termo)
    {
        var hoje = Relogio.Hoje;
        var concluidas = db.Vendas.Where(v => v.Status == StatusVenda.Concluida);
        var lista = await Buscar(db.Clientes.AsNoTracking().Where(c => c.Ativo), termo).OrderBy(c => c.Nome).Take(8)
            .Select(c => new
            {
                c.Id, c.Nome, c.Apelido, c.Telefone, c.Raquete, c.Corda, c.Tensao, c.Nascimento,
                Compras = concluidas.Count(v => v.ClienteId == c.Id),
                Ultima = concluidas.Where(v => v.ClienteId == c.Id).Max(v => (DateTimeOffset?)v.DataHora),
            }).ToListAsync();
        return lista.Select(c =>
        {
            var tel = SoDigitos(c.Telefone);
            return new ClienteBuscaPdv(c.Id, c.Nome, c.Apelido, tel.Length >= 4 ? tel[^4..] : null, c.Compras, c.Ultima,
                c.Raquete, c.Corda, c.Tensao, c.Nascimento is { } n && n.Month == hoje.Month && n.Day == hoje.Day);
        }).ToList();
    }

    // ---------------------------------------------------------------- histórico

    public async Task<ClienteLojaDetalheResponse?> DetalheAsync(int id)
    {
        var c = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return null;

        var vendas = await db.Vendas.AsNoTracking().Where(v => v.ClienteId == id).OrderByDescending(v => v.DataHora)
            .Select(v => new
            {
                v.Id, v.DataHora, v.Total, v.Status, Vendedor = v.Vendedor != null ? v.Vendedor.Nome : null,
                Itens = v.Itens.OrderBy(i => i.Id).Select(i => i.Descricao).ToList(),
                Formas = v.Pagamentos.OrderBy(p => p.Id).Select(p => new { p.Forma, p.Parcelas }).ToList(),
            }).ToListAsync();
        var concluidas = vendas.Where(v => v.Status == StatusVenda.Concluida).ToList();
        var datas = concluidas.Select(v => v.DataHora).OrderBy(d => d).ToList();
        int? entre = datas.Count >= 2 ? (int)Math.Round((datas[^1] - datas[0]).TotalDays / (datas.Count - 1)) : null;
        int? semComprar = datas.Count > 0 ? Relogio.Hoje.DayNumber - Relogio.DiaLocal(datas[^1]).DayNumber : null;

        // Os itens de um cliente são poucos: agrupa aqui (o "vezes" conta vendas, não unidades).
        var maisComprados = (await db.ItensVenda.AsNoTracking()
                .Where(i => i.Venda!.ClienteId == id && i.Venda.Status == StatusVenda.Concluida)
                .Select(i => new { i.Descricao, i.VendaId, i.Quantidade, i.Venda!.DataHora }).ToListAsync())
            .GroupBy(i => i.Descricao)
            .Select(g => new ProdutoDoCliente(g.Key, g.Sum(i => i.Quantidade), g.Select(i => i.VendaId).Distinct().Count(), g.Max(i => i.DataHora)))
            .OrderByDescending(p => p.Vezes).ThenByDescending(p => p.UltimaVez).Take(10).ToList();

        var trocas = (await db.Trocas.AsNoTracking().Where(t => t.Venda!.ClienteId == id).OrderByDescending(t => t.DataHora)
                .Select(t => new { t.Id, t.DataHora, t.VendaId, t.Motivo, t.Valor, Itens = t.Itens.Select(i => i.Descricao).ToList(), t.ValeTroca })
                .ToListAsync())
            .Select(t => new TrocaDoCliente(t.Id, t.DataHora, t.VendaId, string.Join(", ", t.Itens), t.Motivo, t.Valor, t.ValeTroca!.Codigo,
                t.ValeTroca.Saldo, TrocaService.Situacao(t.ValeTroca))).ToList();

        var ordens = await db.OrdensServico.AsNoTracking().Where(o => o.ClienteId == id).OrderByDescending(o => o.CriadaEm).Take(30)
            .Select(o => new OrdemDoCliente(o.Id, o.Numero, o.CriadaEm, o.Raquete, o.Corda, o.Tensao, o.Servico, o.Status, o.Valor)).ToListAsync();

        var total = concluidas.Sum(v => v.Total);
        return new ClienteLojaDetalheResponse(Resposta(c), concluidas.Count, total, concluidas.Count == 0 ? 0 : Math.Round(total / concluidas.Count, 2),
            datas.Count > 0 ? datas[0] : null, datas.Count > 0 ? datas[^1] : null, entre, semComprar, maisComprados,
            vendas.Take(50).Select(v => new CompraDoCliente(v.Id, v.DataHora,
                string.Join(", ", v.Itens.Take(4)) + (v.Itens.Count > 4 ? $" e mais {v.Itens.Count - 4}" : ""), v.Itens.Count, v.Total,
                string.Join(" + ", v.Formas.Select(f => f.Forma == FormaPagamento.Credito && f.Parcelas > 1 ? $"Credito {f.Parcelas}x" : f.Forma.ToString())),
                v.Vendedor, v.Status.ToString())).ToList(),
            trocas, ordens);
    }

    public static ClienteResponse Resposta(Cliente c) => new(c.Id, c.Nome, c.Apelido, c.Telefone, c.Cpf, c.Email, c.Endereco, c.Nascimento,
        c.Raquete, c.Corda, c.Tensao, c.Observacao, c.Ativo, c.Simulado, c.CriadoEm);

    public static string SoDigitos(string? t) => new((t ?? "").Where(char.IsDigit).ToArray());
    private static string? Limpo(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
