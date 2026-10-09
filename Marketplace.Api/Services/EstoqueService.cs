using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Erro de regra de negócio (vira 400 com a mensagem para o usuário).
public class EstoqueException(string mensagem) : Exception(mensagem);

// Duas pessoas mexeram no mesmo produto ao mesmo tempo (vira 409 Conflict).
public class ConflitoEstoqueException(string mensagem) : EstoqueException(mensagem);

// TODA mudança de estoque passa por aqui — entrada, perda, ajuste, venda e cancelamento.
// Assim as regras ficam num lugar só: atualizar o saldo, os lotes e gravar a movimentação.
//
// Dois "níveis" de método:
//  • Registrar...Async → faz a operação E salva (usado pelas telas de estoque).
//  • Preparar...Async  → só prepara as mudanças, SEM salvar (usado pela venda, que junta vários
//    produtos + o cupom + os pagamentos e salva tudo de uma vez, numa única transação).
public class EstoqueService(AppDbContext db)
{
    public static readonly string[] MotivosPerda = ["Vencido", "Avariado", "Furto/Extravio", "Consumo interno", "Outro"];

    // ---------------------------------------------------------------- operações das telas

    public async Task<List<MovimentacaoEstoque>> RegistrarEntradaAsync(
        int produtoId, decimal quantidade, decimal? custoUnitario, DateOnly? validade,
        string? observacao, string? usuarioId)
    {
        var produto = await ObterProdutoAtivoAsync(produtoId);
        ValidarQuantidade(produto, quantidade);

        LoteValidade? lote = null;
        if (produto.ControlaValidade)
        {
            if (validade is null)
                throw new EstoqueException("Produto perecível: informe a data de validade.");
            if (validade < Relogio.Hoje)
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

        var mov = Movimentar(produto, TipoMovimentacao.Entrada, quantidade, usuarioId, lote,
            custoUnitario: custoUnitario, observacao: observacao);
        await SalvarAsync();
        return [mov];
    }

    public async Task<List<MovimentacaoEstoque>> RegistrarPerdaAsync(
        int produtoId, decimal quantidade, string motivo, int? loteId, string? observacao, string? usuarioId)
    {
        if (!MotivosPerda.Contains(motivo))
            throw new EstoqueException($"Motivo inválido. Use: {string.Join(", ", MotivosPerda)}.");

        var produto = await ObterProdutoAtivoAsync(produtoId);
        ValidarQuantidade(produto, quantidade);

        if (quantidade > produto.EstoqueAtual)
            throw new EstoqueException($"A perda ({quantidade:0.###}) é maior que o estoque atual ({produto.EstoqueAtual:0.###}).");

        List<MovimentacaoEstoque> movs;
        if (produto.ControlaValidade && loteId is not null)
        {
            // Perda de um lote específico (ex.: jogar fora o lote que venceu).
            var lote = await db.Lotes.FirstOrDefaultAsync(l => l.Id == loteId && l.ProdutoId == produtoId)
                ?? throw new EstoqueException("Lote não encontrado para este produto.");
            if (quantidade > lote.QuantidadeAtual)
                throw new EstoqueException($"O lote só tem {lote.QuantidadeAtual:0.###}.");
            lote.QuantidadeAtual -= quantidade;
            movs = [Movimentar(produto, TipoMovimentacao.Perda, -quantidade, usuarioId, lote, motivo: motivo, observacao: observacao)];
        }
        else
        {
            movs = await PrepararSaidaAsync(produto, quantidade, TipoMovimentacao.Perda, usuarioId,
                motivo: motivo, observacao: observacao);
        }

        await SalvarAsync();
        return movs;
    }

    // Inventário: a pessoa contou a prateleira e informa quanto TEM de verdade.
    public async Task<List<MovimentacaoEstoque>> RegistrarAjusteAsync(
        int produtoId, decimal quantidadeContada, DateOnly? validade, string observacao, string? usuarioId)
    {
        var produto = await ObterProdutoAtivoAsync(produtoId);
        if (quantidadeContada < 0)
            throw new EstoqueException("A quantidade contada não pode ser negativa.");
        if (!Fracionado(produto) && quantidadeContada % 1 != 0)
            throw new EstoqueException($"Produto vendido por {produto.Unidade}: use número inteiro.");

        var diferenca = quantidadeContada - produto.EstoqueAtual;
        if (diferenca == 0)
            throw new EstoqueException("A contagem é igual ao estoque do sistema: nada a ajustar.");

        List<MovimentacaoEstoque> movs;
        if (diferenca < 0)
        {
            movs = await PrepararSaidaAsync(produto, -diferenca, TipoMovimentacao.Ajuste, usuarioId, observacao: observacao);
        }
        else
        {
            LoteValidade? lote = null;
            if (produto.ControlaValidade)
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
            movs = [Movimentar(produto, TipoMovimentacao.Ajuste, diferenca, usuarioId, lote, observacao: observacao)];
        }

        await SalvarAsync();
        return movs;
    }

    // ---------------------------------------------------------------- usados pela venda

    // Prepara a baixa de estoque de um item vendido (não salva).
    public async Task PrepararVendaAsync(Produto produto, decimal quantidade, Venda venda, string usuarioId)
    {
        ValidarQuantidade(produto, quantidade);

        // Produto vencido NÃO pode ser vendido: só conta o que está dentro da validade.
        var vencido = produto.ControlaValidade
            ? await db.Lotes.Where(l => l.ProdutoId == produto.Id && l.QuantidadeAtual > 0 && l.DataValidade < Relogio.Hoje)
                .SumAsync(l => (decimal?)l.QuantidadeAtual) ?? 0
            : 0;
        var disponivel = produto.EstoqueAtual - vencido;

        if (quantidade > disponivel)
            throw new EstoqueException(vencido > 0
                ? $"\"{produto.Nome}\": só {disponivel:0.###} {produto.Unidade} dentro da validade ({vencido:0.###} vencido(s) — dar baixa no estoque)."
                : $"Estoque insuficiente de \"{produto.Nome}\": tem {produto.EstoqueAtual:0.###} {produto.Unidade}.");

        await PrepararSaidaAsync(produto, quantidade, TipoMovimentacao.Venda, usuarioId, venda: venda, ignorarVencidos: true);
    }

    // Prepara a devolução de uma venda cancelada (não salva): desfaz cada movimentação,
    // devolvendo ao MESMO lote de onde saiu.
    public async Task PrepararCancelamentoAsync(Venda venda, string usuarioId, string motivo)
    {
        var saidas = await db.Movimentacoes
            .Include(m => m.Produto)
            .Include(m => m.Lote)
            .Where(m => m.VendaId == venda.Id && m.Tipo == TipoMovimentacao.Venda)
            .OrderBy(m => m.Id)
            .ToListAsync();

        foreach (var saida in saidas)
        {
            var devolver = -saida.Quantidade; // a saída foi negativa
            if (saida.Lote is not null)
                saida.Lote.QuantidadeAtual += devolver;
            Movimentar(saida.Produto!, TipoMovimentacao.Cancelamento, devolver, usuarioId, saida.Lote,
                observacao: $"Cancelamento da venda #{venda.Id}: {motivo}", venda: venda);
        }
    }

    // ---------------------------------------------------------------- regras internas

    // Saída de estoque. Perecível: FEFO ("First Expired, First Out" — sai primeiro o que vence primeiro),
    // gerando UMA movimentação por lote usado (ex.: 4 do lote que vence dia 10 + 6 do que vence dia 15).
    private async Task<List<MovimentacaoEstoque>> PrepararSaidaAsync(
        Produto produto, decimal quantidade, TipoMovimentacao tipo, string? usuarioId,
        string? motivo = null, string? observacao = null, Venda? venda = null, bool ignorarVencidos = false)
    {
        var movs = new List<MovimentacaoEstoque>();
        var hoje = Relogio.Hoje;
        var restante = quantidade;

        if (produto.ControlaValidade)
        {
            var lotes = await db.Lotes
                .Where(l => l.ProdutoId == produto.Id && l.QuantidadeAtual > 0)
                .Where(l => !ignorarVencidos || l.DataValidade >= hoje) // venda pula os vencidos
                .OrderBy(l => l.DataValidade).ThenBy(l => l.Id)
                .ToListAsync();

            foreach (var lote in lotes)
            {
                if (restante <= 0) break;
                var parte = Math.Min(lote.QuantidadeAtual, restante);
                lote.QuantidadeAtual -= parte;
                restante -= parte;
                movs.Add(Movimentar(produto, tipo, -parte, usuarioId, lote, motivo: motivo, observacao: observacao, venda: venda));
            }
        }

        // Produto não perecível — ou perecível cujos lotes não cobrem tudo (passou a ser perecível depois).
        if (restante > 0)
            movs.Add(Movimentar(produto, tipo, -restante, usuarioId, lote: null, motivo: motivo, observacao: observacao, venda: venda));

        return movs;
    }

    private MovimentacaoEstoque Movimentar(
        Produto produto, TipoMovimentacao tipo, decimal quantidade, string? usuarioId, LoteValidade? lote,
        decimal? custoUnitario = null, string? motivo = null, string? observacao = null, Venda? venda = null)
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
            Venda = venda,
            UsuarioId = usuarioId,
        };

        produto.EstoqueAtual += quantidade;
        produto.AtualizadoEm = DateTimeOffset.UtcNow;
        db.Movimentacoes.Add(movimentacao);
        return movimentacao;
    }

    // Um único SaveChanges = uma única transação: ou grava tudo (saldo + lotes + histórico), ou nada.
    private async Task SalvarAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflitoEstoqueException("O estoque deste produto acabou de ser alterado por outra pessoa. Confira e tente de novo.");
        }
    }

    private async Task<Produto> ObterProdutoAtivoAsync(int produtoId)
    {
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId)
            ?? throw new EstoqueException("Produto não encontrado.");
        if (!produto.Ativo)
            throw new EstoqueException("Produto desativado: reative antes de movimentar o estoque.");
        return produto;
    }

    private static bool Fracionado(Produto produto) =>
        produto.Unidade is Unidades.Quilo or Unidades.Litro;

    private static void ValidarQuantidade(Produto produto, decimal quantidade)
    {
        if (quantidade <= 0)
            throw new EstoqueException("A quantidade deve ser maior que zero.");
        // Arroz se vende por unidade (não existe meio pacote); banana, por quilo (1,235 kg).
        if (!Fracionado(produto) && quantidade % 1 != 0)
            throw new EstoqueException($"\"{produto.Nome}\" é vendido por {produto.Unidade}: use número inteiro.");
    }
}
