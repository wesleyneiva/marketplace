using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Erro de regra de negócio (vira 400 com a mensagem para o usuário).
public class EstoqueException(string mensagem) : Exception(mensagem);

// Duas pessoas mexeram no mesmo produto ao mesmo tempo (vira 409 Conflict).
public class ConflitoEstoqueException(string mensagem) : EstoqueException(mensagem);

// TODA mudança de estoque passa por aqui — entrada, perda, ajuste e, no futuro, a venda do PDV.
// Assim as regras ficam num lugar só: atualizar o saldo, os lotes e gravar a movimentação.
public class EstoqueService(AppDbContext db)
{
    public static readonly string[] MotivosPerda = ["Vencido", "Avariado", "Furto/Extravio", "Consumo interno", "Outro"];

    public async Task<MovimentacaoEstoque> RegistrarEntradaAsync(
        int produtoId, decimal quantidade, decimal? custoUnitario, DateOnly? validade,
        string? observacao, string? usuarioId)
    {
        var produto = await ObterProdutoAsync(produtoId);
        ValidarQuantidade(produto, quantidade);

        LoteValidade? lote = null;
        if (produto.ControlaValidade)
        {
            if (validade is null)
                throw new EstoqueException("Produto perecível: informe a data de validade.");
            if (validade < Relogio.HojeBrasilia)
                throw new EstoqueException("A data de validade já passou.");

            // Mesma validade de um lote que ainda tem saldo → soma nele; senão, cria um lote novo.
            lote = await db.Lotes.FirstOrDefaultAsync(l =>
                l.ProdutoId == produtoId && l.DataValidade == validade && l.QuantidadeAtual > 0);
            if (lote is null)
            {
                lote = new LoteValidade { ProdutoId = produtoId, DataValidade = validade.Value };
                db.Lotes.Add(lote);
            }
            lote.QuantidadeInicial += quantidade;
            lote.QuantidadeAtual += quantidade;
        }

        // Comprou por um preço diferente? O custo do produto passa a ser o da última compra.
        if (custoUnitario is > 0)
            produto.PrecoCusto = custoUnitario.Value;

        return await RegistrarAsync(produto, TipoMovimentacao.Entrada, quantidade, usuarioId,
            custoUnitario: custoUnitario, observacao: observacao, lote: lote);
    }

    public async Task<MovimentacaoEstoque> RegistrarPerdaAsync(
        int produtoId, decimal quantidade, string motivo, int? loteId, string? observacao, string? usuarioId)
    {
        if (!MotivosPerda.Contains(motivo))
            throw new EstoqueException($"Motivo inválido. Use: {string.Join(", ", MotivosPerda)}.");

        var produto = await ObterProdutoAsync(produtoId);
        ValidarQuantidade(produto, quantidade);

        if (quantidade > produto.EstoqueAtual)
            throw new EstoqueException($"A perda ({quantidade:0.###}) é maior que o estoque atual ({produto.EstoqueAtual:0.###}).");

        LoteValidade? loteUsado = null;
        if (produto.ControlaValidade)
        {
            if (loteId is not null)
            {
                // Perda de um lote específico (ex.: jogar fora o lote que venceu).
                loteUsado = await db.Lotes.FirstOrDefaultAsync(l => l.Id == loteId && l.ProdutoId == produtoId)
                    ?? throw new EstoqueException("Lote não encontrado para este produto.");
                if (quantidade > loteUsado.QuantidadeAtual)
                    throw new EstoqueException($"O lote só tem {loteUsado.QuantidadeAtual:0.###}.");
                loteUsado.QuantidadeAtual -= quantidade;
            }
            else
            {
                await BaixarLotesPrimeiroQueVenceAsync(produtoId, quantidade);
            }
        }

        return await RegistrarAsync(produto, TipoMovimentacao.Perda, -quantidade, usuarioId,
            motivo: motivo, observacao: observacao, lote: loteUsado);
    }

    // Inventário: a pessoa contou a prateleira e informa quanto TEM de verdade.
    public async Task<MovimentacaoEstoque> RegistrarAjusteAsync(
        int produtoId, decimal quantidadeContada, DateOnly? validade, string observacao, string? usuarioId)
    {
        var produto = await ObterProdutoAsync(produtoId);
        if (quantidadeContada < 0)
            throw new EstoqueException("A quantidade contada não pode ser negativa.");
        if (produto.Unidade != Unidades.Quilo && produto.Unidade != Unidades.Litro && quantidadeContada % 1 != 0)
            throw new EstoqueException($"Produto vendido por {produto.Unidade}: use número inteiro.");

        var diferenca = quantidadeContada - produto.EstoqueAtual;
        if (diferenca == 0)
            throw new EstoqueException("A contagem é igual ao estoque do sistema: nada a ajustar.");

        LoteValidade? lote = null;
        if (produto.ControlaValidade)
        {
            if (diferenca < 0)
            {
                await BaixarLotesPrimeiroQueVenceAsync(produtoId, -diferenca);
            }
            else
            {
                // Sobrou mercadoria: precisa saber a validade dela.
                if (validade is null)
                    throw new EstoqueException("Produto perecível: informe a validade das unidades a mais.");
                lote = new LoteValidade
                {
                    ProdutoId = produtoId, DataValidade = validade.Value,
                    QuantidadeInicial = diferenca, QuantidadeAtual = diferenca,
                };
                db.Lotes.Add(lote);
            }
        }

        return await RegistrarAsync(produto, TipoMovimentacao.Ajuste, diferenca, usuarioId,
            observacao: observacao, lote: lote);
    }

    // FEFO = "First Expired, First Out": sai primeiro o que vence primeiro.
    // É assim que o mercado evita jogar comida fora.
    public async Task BaixarLotesPrimeiroQueVenceAsync(int produtoId, decimal quantidade)
    {
        var lotes = await db.Lotes
            .Where(l => l.ProdutoId == produtoId && l.QuantidadeAtual > 0)
            .OrderBy(l => l.DataValidade)
            .ToListAsync();

        var restante = quantidade;
        foreach (var lote in lotes)
        {
            if (restante <= 0) break;
            var baixa = Math.Min(lote.QuantidadeAtual, restante);
            lote.QuantidadeAtual -= baixa;
            restante -= baixa;
        }
        // Se os lotes não cobrirem tudo (produto que passou a ser perecível depois), segue assim mesmo.
    }

    private async Task<Produto> ObterProdutoAsync(int produtoId)
    {
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId)
            ?? throw new EstoqueException("Produto não encontrado.");
        if (!produto.Ativo)
            throw new EstoqueException("Produto desativado: reative antes de movimentar o estoque.");
        return produto;
    }

    private static void ValidarQuantidade(Produto produto, decimal quantidade)
    {
        if (quantidade <= 0)
            throw new EstoqueException("A quantidade deve ser maior que zero.");
        // Arroz se vende por unidade (não existe meio pacote); banana, por quilo (12,350 kg).
        if (produto.Unidade != Unidades.Quilo && produto.Unidade != Unidades.Litro && quantidade % 1 != 0)
            throw new EstoqueException($"Produto vendido por {produto.Unidade}: use número inteiro.");
    }

    private async Task<MovimentacaoEstoque> RegistrarAsync(
        Produto produto, TipoMovimentacao tipo, decimal quantidade, string? usuarioId,
        decimal? custoUnitario = null, string? motivo = null, string? observacao = null, LoteValidade? lote = null)
    {
        var movimentacao = new MovimentacaoEstoque
        {
            Produto = produto,
            Tipo = tipo,
            Quantidade = quantidade,
            EstoqueAnterior = produto.EstoqueAtual,
            EstoquePosterior = produto.EstoqueAtual + quantidade,
            CustoUnitario = custoUnitario,
            Motivo = motivo,
            Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim(),
            Lote = lote,
            UsuarioId = usuarioId,
        };

        produto.EstoqueAtual += quantidade;
        produto.AtualizadoEm = DateTimeOffset.UtcNow;
        db.Movimentacoes.Add(movimentacao);

        // Um único SaveChanges = uma única transação: ou grava tudo (saldo + lote + histórico), ou nada.
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflitoEstoqueException("O estoque deste produto acabou de ser alterado por outra pessoa. Confira e tente de novo.");
        }

        return movimentacao;
    }
}
