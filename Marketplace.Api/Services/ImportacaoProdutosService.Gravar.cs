using Marketplace.Api.Contracts;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

public class ImportacaoException(string mensagem) : Exception(mensagem);

// Passo 4: grava. O servidor LÊ E CONFERE A PLANILHA DE NOVO (não confia na prévia que a tela tem) e grava
// só as linhas sem erro. Tudo vai num único SaveChanges, que o EF faz dentro de uma transação:
// ou entra tudo, ou nada (nunca fica "metade importada").
public partial class ImportacaoProdutosService
{
    public async Task<ResultadoImportacaoResponse> ImportarAsync(Stream arquivo, string nomeDoArquivo, string? usuarioId)
    {
        var analise = await AnalisarAsync(arquivo, nomeDoArquivo);
        if (analise.ErrosGerais.Count > 0)
            throw new ImportacaoException(analise.ErrosGerais[0]);

        var validas = analise.Linhas.Where(l => l.Erros.Count == 0).ToList();
        if (validas.Count == 0)
            throw new ImportacaoException("Nenhuma linha pode ser importada: corrija os erros e envie de novo.");

        // Categorias: as que já existem + as novas (criadas junto, no mesmo SaveChanges).
        var categorias = (await db.Categorias.ToListAsync()).GroupBy(c => Normalizar(c.Nome)).ToDictionary(g => g.Key, g => g.First());
        var criadas = new List<string>();
        foreach (var nome in analise.CategoriasNovas)
        {
            var nova = new Categoria { Nome = nome };
            db.Categorias.Add(nova);
            categorias[Normalizar(nome)] = nova;
            criadas.Add(nome);
        }

        var idsExistentes = validas.Where(l => l.ProdutoExistenteId is not null).Select(l => l.ProdutoExistenteId!.Value).ToList();
        var existentes = await db.Produtos.Where(p => idsExistentes.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var agora = DateTimeOffset.UtcNow;
        var observacao = $"Estoque inicial (importação: {Cortar(nomeDoArquivo, 120)})";
        int novos = 0, atualizados = 0, comEstoque = 0;

        foreach (var l in validas)
        {
            var categoria = categorias[Normalizar(l.Categoria!)];

            if (l.ProdutoExistenteId is int id)
            {
                // Atualiza. Célula opcional VAZIA = mantém o valor atual (não zera).
                var p = existentes[id];
                p.Nome = l.Nome!;
                p.Categoria = categoria;
                p.Unidade = l.Unidade!;
                p.PrecoVenda = l.PrecoVenda!.Value;
                if (l.CodigoBarras is not null) p.CodigoBarras = l.CodigoBarras;
                if (l.PrecoCusto is decimal custo) p.PrecoCusto = custo;
                if (l.EstoqueMinimo is decimal minimo) p.EstoqueMinimo = minimo;
                if (l.ControlaValidade is bool controla) p.ControlaValidade = controla;
                p.Ativo = true;
                p.AtualizadoEm = agora;
                atualizados++;
                continue;
            }

            var produto = new Produto
            {
                CodigoBarras = l.CodigoBarras,
                Nome = l.Nome!,
                Categoria = categoria,
                Unidade = l.Unidade!,
                PrecoCusto = l.PrecoCusto ?? 0,
                PrecoVenda = l.PrecoVenda!.Value,
                EstoqueMinimo = l.EstoqueMinimo ?? 0,
                ControlaValidade = l.ControlaValidade ?? false,
            };
            db.Produtos.Add(produto);
            novos++;

            // Estoque inicial: vira uma movimentação "Inventário" (o extrato do produto começa certo)
            // e, se controla validade, um lote com a data informada.
            if (l.EstoqueInicial is decimal quantidade && quantidade > 0)
            {
                LoteValidade? lote = null;
                if (produto.ControlaValidade)
                {
                    lote = new LoteValidade
                    {
                        Produto = produto, DataValidade = l.Validade!.Value,
                        QuantidadeInicial = quantidade, QuantidadeAtual = quantidade,
                    };
                    db.Lotes.Add(lote);
                }
                db.Movimentacoes.Add(new MovimentacaoEstoque
                {
                    Produto = produto,
                    Tipo = TipoMovimentacao.Inventario,
                    Quantidade = quantidade,
                    EstoqueAnterior = 0,
                    EstoquePosterior = quantidade,
                    CustoUnitario = produto.PrecoCusto > 0 ? produto.PrecoCusto : null,
                    Observacao = observacao,
                    Lote = lote,
                    UsuarioId = usuarioId,
                });
                produto.EstoqueAtual = quantidade;
                comEstoque++;
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Ex.: alguém cadastrou o mesmo código de barras pela tela enquanto a importação rodava,
            // ou editou um dos produtos ao mesmo tempo (xmin). Nada foi gravado.
            throw new ImportacaoException(
                "Alguém mexeu nos produtos ao mesmo tempo e nada foi gravado. Envie a planilha de novo.");
        }

        return new(novos, atualizados, analise.Linhas.Count - validas.Count, criadas, comEstoque);
    }

    private static string Cortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
