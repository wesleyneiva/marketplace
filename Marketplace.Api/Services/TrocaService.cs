using System.Security.Cryptography;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Troca e vale-troca (ver Models/Troca.cs). A troca acontece no caixa aberto: os itens voltam ao estoque e o valor pago
// por eles vira um vale-troca. O produto novo é uma venda comum paga com o vale (o que passar, o cliente paga; o que
// sobrar, fica no vale). Sem dinheiro saindo da gaveta: quem quer o dinheiro de volta é cancelamento (estorno).
public class TrocaService(AppDbContext db, EstoqueService estoque, CaixaService caixa, AutorizacaoService autorizacoes)
{
    // ---------------------------------------------------------------- achar a venda

    public async Task<VendaParaTrocaResponse> ConsultarVendaAsync(int vendaId)
    {
        var venda = await db.Vendas.AsNoTracking().Include(v => v.Itens).Include(v => v.SessaoCaixa)
            .FirstOrDefaultAsync(v => v.Id == vendaId) ?? throw new EstoqueException($"Venda #{vendaId} não encontrada.");
        var prazo = await PrazoAsync();
        var dias = Relogio.Hoje.DayNumber - Relogio.DiaLocal(venda.DataHora).DayNumber;
        var devolvido = await DevolvidoAsync(venda.Id);
        var fator = Fator(venda);
        var itens = venda.Itens.OrderBy(i => i.Id).Select(i =>
        {
            var ja = devolvido.GetValueOrDefault(i.Id);
            return new ItemParaTroca(i.Id, i.ProdutoId, i.Descricao, i.Unidade, i.Quantidade, ja.Quantidade, i.Quantidade - ja.Quantidade,
                i.Quantidade == 0 ? 0 : Dinheiro(i.Total * fator / i.Quantidade));
        }).ToList();
        string? bloqueio = venda.Status == StatusVenda.Cancelada ? "Esta venda foi cancelada: não há o que trocar."
            : venda.Origem == OrigemVenda.Historico ? "Venda do histórico gerado: não dá para trocar."
            : itens.All(i => i.Disponivel <= 0) ? "Todos os itens desta venda já foram trocados."
            : null;
        return new VendaParaTrocaResponse(venda.Id, venda.DataHora, venda.SessaoCaixa!.NumeroCaixa, venda.Total, dias, prazo,
            dias <= prazo, bloqueio, itens);
    }

    // O cliente veio sem o cupom: as vendas recentes com o produto que ele está devolvendo (a mais nova primeiro).
    public async Task<List<VendaComProdutoResponse>> VendasComProdutoAsync(int produtoId)
    {
        var prazo = await PrazoAsync();
        var desde = Relogio.InicioDoDiaUtc(Relogio.Hoje.AddDays(-Math.Max(prazo * 2, 60)));
        var limite = Relogio.Hoje.AddDays(-prazo);
        var vendas = await db.Vendas.AsNoTracking()
            .Where(v => v.Status == StatusVenda.Concluida && v.Origem != OrigemVenda.Historico && v.DataHora >= desde
                        && v.Itens.Any(i => i.ProdutoId == produtoId))
            .OrderByDescending(v => v.Id).Take(20)
            .Select(v => new { v.Id, v.DataHora, v.SessaoCaixa!.NumeroCaixa, v.Total, Itens = v.Itens.OrderBy(i => i.Id).Select(i => i.Descricao).ToList() })
            .ToListAsync();
        return vendas.Select(v => new VendaComProdutoResponse(v.Id, v.DataHora, v.NumeroCaixa, v.Total,
            string.Join(" · ", v.Itens.Take(3)) + (v.Itens.Count > 3 ? $" e mais {v.Itens.Count - 3}" : ""),
            Relogio.DiaLocal(v.DataHora) >= limite)).ToList();
    }

    // ---------------------------------------------------------------- registrar

    // Fora do prazo precisa de gerente: o controller confere a senha ANTES (ver PrecisaGerenteAsync) e manda quem autorizou.
    public async Task<bool> ForaDoPrazoAsync(int vendaId)
    {
        var data = await db.Vendas.Where(v => v.Id == vendaId).Select(v => (DateTimeOffset?)v.DataHora).FirstOrDefaultAsync()
                   ?? throw new EstoqueException($"Venda #{vendaId} não encontrada.");
        return Relogio.Hoje.DayNumber - Relogio.DiaLocal(data).DayNumber > await PrazoAsync();
    }

    public async Task<Troca> RegistrarAsync(NovaTrocaRequest r, string usuarioId, string? autorizadoPorId)
    {
        var sessao = await caixa.ObterSessaoAbertaAsync(usuarioId) ?? throw new EstoqueException("Abra o caixa antes de fazer a troca.");
        if (!MotivosTroca.Todos.Contains(r.Motivo)) throw new EstoqueException("Escolha o motivo da troca.");

        await using var transacao = await db.Database.BeginTransactionAsync();
        // Trava a venda: duas trocas da mesma venda ao mesmo tempo não devolvem o mesmo item duas vezes.
        var venda = (await db.Vendas.FromSql($"SELECT * FROM \"Vendas\" WHERE \"Id\" = {r.VendaId} AND \"EmpresaId\" = {db.EmpresaAtual} FOR UPDATE")
            .ToListAsync()).FirstOrDefault() ?? throw new EstoqueException($"Venda #{r.VendaId} não encontrada.");
        await db.Entry(venda).Collection(v => v.Itens).LoadAsync();
        if (venda.Status == StatusVenda.Cancelada) throw new EstoqueException("Esta venda foi cancelada: não há o que trocar.");
        if (venda.Origem == OrigemVenda.Historico) throw new EstoqueException("Venda do histórico gerado: não dá para trocar.");

        var foraDoPrazo = Relogio.Hoje.DayNumber - Relogio.DiaLocal(venda.DataHora).DayNumber > await PrazoAsync();
        if (foraDoPrazo && autorizadoPorId is null) throw new EstoqueException("Passou do prazo de troca: precisa da senha de um gerente.");

        var devolvido = await DevolvidoAsync(venda.Id);
        var ids = r.Itens.Select(i => i.ItemVendaId).ToHashSet();
        var produtoIds = venda.Itens.Where(i => ids.Contains(i.Id)).Select(i => i.ProdutoId).Distinct().ToArray();
        var produtos = await db.Produtos
            .FromSql($"SELECT *, xmin FROM \"Produtos\" WHERE \"Id\" = ANY({produtoIds}) AND \"EmpresaId\" = {db.EmpresaAtual} ORDER BY \"Id\" FOR UPDATE")
            .ToDictionaryAsync(p => p.Id);

        var troca = new Troca
        {
            Venda = venda, SessaoCaixaId = sessao.Id, UsuarioId = usuarioId, Motivo = r.Motivo, ForaDoPrazo = foraDoPrazo,
            Observacao = string.IsNullOrWhiteSpace(r.Observacao) ? null : r.Observacao.Trim(),
        };
        var fator = Fator(venda);
        foreach (var pedido in r.Itens.GroupBy(i => i.ItemVendaId).Select(g => (Id: g.Key, Quantidade: g.Sum(i => i.Quantidade))))
        {
            var item = venda.Itens.FirstOrDefault(i => i.Id == pedido.Id) ?? throw new EstoqueException("Um dos itens não é desta venda.");
            var ja = devolvido.GetValueOrDefault(item.Id);
            var disponivel = item.Quantidade - ja.Quantidade;
            if (pedido.Quantidade > disponivel)
                throw new EstoqueException(disponivel <= 0 ? $"\"{item.Descricao}\" já foi todo trocado."
                    : $"\"{item.Descricao}\": dá para devolver no máximo {disponivel:0.###}.");
            if (item.Unidade is not (Unidades.Quilo or Unidades.Litro) && pedido.Quantidade % 1 != 0)
                throw new EstoqueException($"\"{item.Descricao}\" é vendido por {item.Unidade}: use número inteiro.");

            // Crédito = o que o cliente pagou (com a parte dele no desconto da venda). Devolvendo o resto do item, fecha a
            // conta exata (sem sobra de centavo por arredondamento entre trocas parciais).
            var creditoDoItem = Dinheiro(item.Total * fator);
            var valor = pedido.Quantidade == disponivel ? creditoDoItem - ja.Valor : Dinheiro(creditoDoItem * pedido.Quantidade / item.Quantidade);
            troca.Itens.Add(new ItemTroca
            {
                ItemVendaId = item.Id, ProdutoId = item.ProdutoId, Descricao = item.Descricao, Quantidade = pedido.Quantidade,
                Valor = valor, CustoUnitario = item.CustoUnitario,
            });
            await estoque.PrepararTrocaAsync(venda, produtos[item.ProdutoId], pedido.Quantidade, usuarioId,
                $"Troca da venda #{venda.Id} ({r.Motivo.ToLowerInvariant()})");
        }
        troca.Valor = troca.Itens.Sum(i => i.Valor);
        if (troca.Valor <= 0) throw new EstoqueException("Marque o que o cliente está devolvendo.");

        troca.ValeTroca = new ValeTroca
        {
            Codigo = await NovoCodigoAsync(), Valor = troca.Valor, Saldo = troca.Valor,
            Validade = Relogio.Hoje.AddDays(await db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.ValidadeValeDias).FirstAsync()),
            Cliente = string.IsNullOrWhiteSpace(r.Cliente) ? null : r.Cliente.Trim(),
        };
        db.Trocas.Add(troca);
        if (foraDoPrazo)
            autorizacoes.Registrar(AcaoAutorizada.TrocaForaPrazo, usuarioId, autorizadoPorId!, sessao.Id, r.Observacao ?? r.Motivo,
                $"Troca da venda #{venda.Id} ({Relogio.DiaLocal(venda.DataHora):dd/MM}): " + string.Join("; ", troca.Itens.Select(i => $"{i.Quantidade:0.###} × {i.Descricao}")),
                troca.Valor, venda);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return troca;
    }

    // ---------------------------------------------------------------- vale

    public async Task<ValeTrocaResponse> ValePorCodigoAsync(string codigo)
    {
        var c = NormalizarCodigo(codigo);
        var vale = await db.ValesTroca.AsNoTracking().FirstOrDefaultAsync(v => v.Codigo == c)
                   ?? throw new EstoqueException($"Vale-troca {c} não encontrado.");
        return await MontarValeAsync(vale);
    }

    // Usa os vales de uma venda (chamado pelo VendaService, dentro da transação da venda). FOR UPDATE: o mesmo vale
    // não paga duas vendas ao mesmo tempo em dois caixas.
    public async Task UsarValesAsync(Venda venda)
    {
        var usados = venda.Pagamentos.Where(p => p.Forma == FormaPagamento.ValeTroca).ToList();
        if (usados.Count == 0) return;
        if (usados.Any(p => p.ValeTrocaId is null)) throw new EstoqueException("Informe o código do vale-troca.");
        var ids = usados.Select(p => p.ValeTrocaId!.Value).Distinct().ToArray();
        var vales = await db.ValesTroca
            .FromSql($"SELECT * FROM \"ValesTroca\" WHERE \"Id\" = ANY({ids}) AND \"EmpresaId\" = {db.EmpresaAtual} FOR UPDATE")
            .ToDictionaryAsync(v => v.Id);
        foreach (var p in usados)
        {
            if (!vales.TryGetValue(p.ValeTrocaId!.Value, out var vale)) throw new EstoqueException("Vale-troca não encontrado.");
            if (vale.Validade < Relogio.Hoje) throw new EstoqueException($"O vale-troca {vale.Codigo} venceu em {vale.Validade:dd/MM/yyyy}.");
            if (p.Valor > vale.Saldo) throw new EstoqueException($"O vale-troca {vale.Codigo} tem só {vale.Saldo:C}.");
            vale.Saldo -= p.Valor;
            p.ValeTroca = vale;
        }
    }

    // Venda paga com vale foi cancelada: o valor volta para o vale (com a validade que ele já tinha).
    public async Task DevolverAoValeAsync(Venda venda)
    {
        foreach (var p in await db.PagamentosVenda.Include(p => p.ValeTroca)
                     .Where(p => p.VendaId == venda.Id && p.ValeTrocaId != null).ToListAsync())
            p.ValeTroca!.Saldo += p.Valor;
    }

    public static string NormalizarCodigo(string codigo)
    {
        var c = new string(codigo.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (c.StartsWith("VT")) c = c[2..];
        return "VT-" + c;
    }

    // ---------------------------------------------------------------- relatório

    public async Task<TrocasRelatorioResponse> RelatorioAsync(DateOnly de, DateOnly ate)
    {
        var (ini, fim) = (Relogio.InicioDoDiaUtc(de), Relogio.InicioDoDiaUtc(ate.AddDays(1)));
        var trocas = await db.Trocas.AsNoTracking().Include(t => t.Itens).Include(t => t.ValeTroca)
            .Include(t => t.SessaoCaixa).Include(t => t.Usuario)
            .Where(t => t.DataHora >= ini && t.DataHora < fim).OrderByDescending(t => t.Id).ToListAsync();
        var lista = new List<TrocaResponse>();
        foreach (var t in trocas) lista.Add(await MontarAsync(t));

        var hoje = Relogio.Hoje;
        var abertos = await db.ValesTroca.AsNoTracking().Where(v => v.Saldo > 0 && v.Validade >= hoje).ToListAsync();
        var vencidos = await db.ValesTroca.AsNoTracking().Where(v => v.Saldo > 0 && v.Validade >= de.AddDays(-1) && v.Validade < ate && v.Validade < hoje)
            .SumAsync(v => (decimal?)v.Saldo) ?? 0;
        var vales = new List<ValeTrocaResponse>();
        foreach (var v in abertos.OrderBy(v => v.Validade)) vales.Add(await MontarValeAsync(v));

        return new TrocasRelatorioResponse(de, ate, trocas.Count, trocas.Sum(t => t.Valor),
            trocas.GroupBy(t => t.Motivo).Select(g => new TrocasPorMotivo(g.Key, g.Count(), g.Sum(t => t.Valor))).OrderByDescending(m => m.Quantidade).ToList(),
            trocas.SelectMany(t => t.Itens).GroupBy(i => i.Descricao)
                .Select(g => new ProdutoTrocado(g.Key, g.Sum(i => i.Quantidade), g.Sum(i => i.Valor), g.Select(i => i.TrocaId).Distinct().Count()))
                .OrderByDescending(p => p.Trocas).ThenByDescending(p => p.Valor).Take(15).ToList(),
            lista, abertos.Count, abertos.Sum(v => v.Saldo), vencidos, vales);
    }

    public async Task<TrocaResponse> ObterAsync(int id)
    {
        var t = await db.Trocas.AsNoTracking().Include(t => t.Itens).Include(t => t.ValeTroca).Include(t => t.SessaoCaixa).Include(t => t.Usuario)
            .FirstOrDefaultAsync(t => t.Id == id) ?? throw new EstoqueException("Troca não encontrada.");
        return await MontarAsync(t);
    }

    // ---------------------------------------------------------------- apoio

    private async Task<TrocaResponse> MontarAsync(Troca t) => new(t.Id, t.VendaId, t.DataHora, t.SessaoCaixa!.NumeroCaixa,
        t.Usuario!.NomeCompleto, t.Motivo, t.Observacao, t.Valor, t.ForaDoPrazo, await MontarValeAsync(t.ValeTroca!),
        t.Itens.OrderBy(i => i.Id).Select(i => new ItemTrocaResponse(i.ProdutoId, i.Descricao, i.Quantidade, i.Valor)).ToList());

    private async Task<ValeTrocaResponse> MontarValeAsync(ValeTroca v)
    {
        var troca = await db.Trocas.AsNoTracking().Where(t => t.ValeTrocaId == v.Id).Select(t => new { t.Id, t.VendaId }).FirstOrDefaultAsync();
        return new ValeTrocaResponse(v.Id, v.Codigo, v.Valor, v.Saldo, v.Validade, v.Cliente, Situacao(v), v.CriadoEm, troca?.Id, troca?.VendaId);
    }

    public static string Situacao(ValeTroca v) => v.Saldo <= 0 ? "Usado" : v.Validade < Relogio.Hoje ? "Vencido" : "Disponivel";

    private async Task<int> PrazoAsync() =>
        await db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.PrazoTrocaDias).FirstAsync();

    // Quanto de cada item da venda já voltou em trocas anteriores (quantidade e crédito).
    private async Task<Dictionary<int, (decimal Quantidade, decimal Valor)>> DevolvidoAsync(int vendaId) =>
        (await db.ItensTroca.AsNoTracking().Where(i => i.Troca!.VendaId == vendaId)
            .GroupBy(i => i.ItemVendaId).Select(g => new { g.Key, Qtd = g.Sum(i => i.Quantidade), Valor = g.Sum(i => i.Valor) }).ToListAsync())
        .ToDictionary(x => x.Key, x => (x.Qtd, x.Valor));

    // Parte de cada real do item que o cliente de fato pagou (o desconto da venda é dividido entre os itens).
    private static decimal Fator(Venda v) => v.Subtotal == 0 ? 0 : v.Total / v.Subtotal;

    // VT-XXXXXX sem letras que confundem (0/O, 1/I/L).
    private async Task<string> NovoCodigoAsync()
    {
        const string letras = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        while (true)
        {
            var codigo = "VT-" + new string(Enumerable.Range(0, 6).Select(_ => letras[RandomNumberGenerator.GetInt32(letras.Length)]).ToArray());
            if (!await db.ValesTroca.AnyAsync(v => v.Codigo == codigo)) return codigo;
        }
    }

    private static decimal Dinheiro(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
