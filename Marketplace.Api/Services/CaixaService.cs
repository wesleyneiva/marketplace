using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

public class CaixaException(string mensagem) : Exception(mensagem);

// Abertura, sangria/suprimento e fechamento do caixa — e a conta mais importante:
// quanto dinheiro DEVERIA haver na gaveta.
public class CaixaService(AppDbContext db)
{
    public Task<SessaoCaixa?> ObterSessaoAbertaAsync(string usuarioId) =>
        db.SessoesCaixa.FirstOrDefaultAsync(s => s.UsuarioId == usuarioId && s.Status == StatusSessao.Aberta);

    // Quantos caixas esta empresa pode usar (o "tamanho do plano": 2 no mercadinho, 20 no mercado grande).
    public Task<int> LimiteCaixasAsync() =>
        db.Empresas.Where(e => e.Id == db.EmpresaAtual).Select(e => e.LimiteCaixas).FirstAsync();

    public async Task<List<CaixaDisponivel>> CaixasAsync()
    {
        var limite = await LimiteCaixasAsync();
        var ocupados = await db.SessoesCaixa.AsNoTracking().Where(s => s.Status == StatusSessao.Aberta)
            .Select(s => new { s.NumeroCaixa, s.Usuario!.NomeCompleto }).ToListAsync();
        return Enumerable.Range(1, limite)
            .Select(n => new CaixaDisponivel(n, ocupados.FirstOrDefault(o => o.NumeroCaixa == n)?.NomeCompleto))
            .ToList();
    }

    public async Task<SessaoCaixa> AbrirAsync(string usuarioId, int numeroCaixa, decimal valorAbertura)
    {
        var limite = await LimiteCaixasAsync();
        if (numeroCaixa < 1 || numeroCaixa > limite)
            throw new CaixaException(limite == 1
                ? "Este mercado tem só o Caixa 1."
                : $"Este mercado tem {limite} caixas: escolha de 1 a {limite}.");

        if (await ObterSessaoAbertaAsync(usuarioId) is not null)
            throw new CaixaException("Você já tem um caixa aberto. Feche-o antes de abrir outro.");

        var ocupado = await db.SessoesCaixa.Include(s => s.Usuario)
            .FirstOrDefaultAsync(s => s.NumeroCaixa == numeroCaixa && s.Status == StatusSessao.Aberta);
        if (ocupado is not null)
            throw new CaixaException($"O Caixa {numeroCaixa} já está aberto por {ocupado.Usuario?.NomeCompleto}.");

        var sessao = new SessaoCaixa { NumeroCaixa = numeroCaixa, UsuarioId = usuarioId, ValorAbertura = valorAbertura };
        db.SessoesCaixa.Add(sessao);
        await db.SaveChangesAsync();
        return sessao;
    }

    public async Task MovimentarAsync(string usuarioId, TipoMovimentoCaixa tipo, decimal valor, string motivo)
    {
        var sessao = await ObterSessaoAbertaAsync(usuarioId)
            ?? throw new CaixaException("Você não tem caixa aberto.");

        if (tipo == TipoMovimentoCaixa.Sangria)
        {
            var resumo = await ResumirAsync(sessao.Id);
            if (valor > resumo.DinheiroEsperado)
                throw new CaixaException($"Sangria maior que o dinheiro na gaveta ({resumo.DinheiroEsperado:C}).");
        }

        db.MovimentosCaixa.Add(new MovimentoCaixa
        {
            SessaoCaixaId = sessao.Id, Tipo = tipo, Valor = valor, Motivo = motivo.Trim(), UsuarioId = usuarioId,
        });
        await db.SaveChangesAsync();
    }

    public async Task<ResumoCaixaResponse> FecharAsync(string usuarioId, decimal valorContado, string? observacao)
    {
        var sessao = await ObterSessaoAbertaAsync(usuarioId)
            ?? throw new CaixaException("Você não tem caixa aberto.");

        var resumo = await ResumirAsync(sessao.Id);

        sessao.Status = StatusSessao.Fechada;
        sessao.FechadaEm = DateTimeOffset.UtcNow;
        sessao.ValorEsperado = resumo.DinheiroEsperado;
        sessao.ValorContado = valorContado;
        sessao.Diferenca = valorContado - resumo.DinheiroEsperado;
        sessao.ObservacaoFechamento = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim();
        await db.SaveChangesAsync();

        return await ResumirAsync(sessao.Id);
    }

    // O "relatório X / Z" do caixa.
    public async Task<ResumoCaixaResponse> ResumirAsync(int sessaoId)
    {
        var sessao = await db.SessoesCaixa.AsNoTracking().Include(s => s.Usuario)
            .FirstOrDefaultAsync(s => s.Id == sessaoId) ?? throw new CaixaException("Sessão de caixa não encontrada.");

        var vendas = db.Vendas.AsNoTracking().Where(v => v.SessaoCaixaId == sessaoId);
        var concluidas = vendas.Where(v => v.Status == StatusVenda.Concluida);

        var quantidade = await concluidas.CountAsync();
        var total = await concluidas.SumAsync(v => (decimal?)v.Total) ?? 0;
        var troco = await concluidas.SumAsync(v => (decimal?)v.Troco) ?? 0;

        var porForma = await db.PagamentosVenda.AsNoTracking()
            .Where(p => p.Venda!.SessaoCaixaId == sessaoId && p.Venda.Status == StatusVenda.Concluida)
            .GroupBy(p => p.Forma)
            .Select(g => new { Forma = g.Key, Valor = g.Sum(p => p.Valor) })
            .ToListAsync();

        var movimentos = await db.MovimentosCaixa.AsNoTracking().Where(m => m.SessaoCaixaId == sessaoId)
            .GroupBy(m => m.Tipo).Select(g => new { Tipo = g.Key, Valor = g.Sum(m => m.Valor) }).ToListAsync();
        var sangrias = movimentos.FirstOrDefault(m => m.Tipo == TipoMovimentoCaixa.Sangria)?.Valor ?? 0;
        var suprimentos = movimentos.FirstOrDefault(m => m.Tipo == TipoMovimentoCaixa.Suprimento)?.Valor ?? 0;

        var canceladas = vendas.Where(v => v.Status == StatusVenda.Cancelada);
        var qtdCanceladas = await canceladas.CountAsync();
        var valorCancelado = await canceladas.SumAsync(v => (decimal?)v.Total) ?? 0;

        // Dinheiro na gaveta = troco inicial + suprimentos − sangrias + (dinheiro recebido − troco devolvido).
        var dinheiroRecebido = porForma.FirstOrDefault(f => f.Forma == FormaPagamento.Dinheiro)?.Valor ?? 0;
        var esperado = sessao.ValorAbertura + suprimentos - sangrias + dinheiroRecebido - troco;

        // Por forma de pagamento, o que de fato ficou: no dinheiro, desconta o troco devolvido.
        var formas = Enum.GetValues<FormaPagamento>()
            .Select(f => new TotalPorForma(f.ToString(),
                (porForma.FirstOrDefault(x => x.Forma == f)?.Valor ?? 0) - (f == FormaPagamento.Dinheiro ? troco : 0)))
            .ToList();

        return new ResumoCaixaResponse(
            sessao.Id, sessao.NumeroCaixa, sessao.Usuario?.NomeCompleto ?? "", sessao.Status.ToString(),
            sessao.AbertaEm, sessao.FechadaEm, sessao.ValorAbertura,
            quantidade, total, quantidade == 0 ? 0 : Math.Round(total / quantidade, 2),
            formas, troco, sangrias, suprimentos,
            sessao.Status == StatusSessao.Fechada ? sessao.ValorEsperado ?? esperado : esperado,
            qtdCanceladas, valorCancelado, sessao.ValorContado, sessao.Diferenca, sessao.ObservacaoFechamento);
    }
}
