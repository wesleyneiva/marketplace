using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.NotaFiscal;

public class NotaEntradaException(string mensagem) : Exception(mensagem);

// Entrada de mercadoria pelo XML da NF-e:
//  • Conferir: lê a nota e diz, item a item, qual é o produto (pelo vínculo salvo ou pelo código de barras),
//    quantas unidades entram (fator da caixa) e o custo de cada uma. Não grava nada.
//  • Registrar: com as decisões da pessoa, dá entrada no estoque, cadastra o que for novo e guarda os vínculos
//    para a próxima nota do mesmo fornecedor sair sozinha. Tudo numa transação.
public partial class EntradaNotaService(AppDbContext db, EstoqueService estoque)
{
    public const int TamanhoMaximo = 2 * 1024 * 1024; // XML de nota com centenas de itens tem ~1 MB

    // ===================================================================== conferência

    public async Task<ConferenciaNotaResponse> ConferirAsync(byte[] xml)
    {
        var nota = Ler(xml);
        var avisos = new List<string>();

        var fornecedor = await AcharFornecedorAsync(nota.EmitenteCnpj);
        string? bloqueio = null;
        if (await db.NotasEntrada.AnyAsync(n => n.Chave == nota.Chave))
            bloqueio = "Esta nota já foi lançada no estoque.";
        if (!nota.Autorizada)
            avisos.Add("O XML não tem o protocolo de autorização da SEFAZ. Confira se a nota é válida antes de lançar.");
        if (nota.Modelo == 65)
            avisos.Add("Este é um cupom fiscal (NFC-e), não uma nota de compra de fornecedor.");
        if (nota.TipoOperacao == 0)
            avisos.Add("A nota é de ENTRADA para o emitente (ex.: devolução). Confira se é mesmo uma compra.");

        // Vínculos do fornecedor (se ele já existe) e produtos com código de barras.
        var vinculos = fornecedor is null ? new Dictionary<string, VinculoFornecedorProduto>()
            : await db.VinculosFornecedor.AsNoTracking().Where(v => v.FornecedorId == fornecedor.Id)
                .ToDictionaryAsync(v => v.CodigoFornecedor);
        var codigos = nota.Itens.SelectMany(i => new[] { i.Ean, i.EanTributavel }).OfType<string>().Distinct().ToList();
        var idsVinculados = vinculos.Values.Select(v => v.ProdutoId).ToList();
        var produtos = await db.Produtos.AsNoTracking()
            .Where(p => (p.CodigoBarras != null && codigos.Contains(p.CodigoBarras)) || idsVinculados.Contains(p.Id))
            .ToListAsync();
        var porCodigo = produtos.Where(p => p.CodigoBarras != null).ToDictionary(p => p.CodigoBarras!);
        var porId = produtos.ToDictionary(p => p.Id);

        var categorias = await db.Categorias.AsNoTracking().Where(c => c.Ativa).ToListAsync();
        var margens = await MargensPorCategoriaAsync();
        // Para avisar "já existe um produto com esse nome" (evita cadastrar o mesmo produto duas vezes).
        var nomes = (await db.Produtos.AsNoTracking().Where(p => p.Ativo).Select(p => p.Nome).ToListAsync())
            .GroupBy(ImportacaoProdutosService.Normalizar).ToDictionary(g => g.Key, g => g.First());

        var itens = new List<ItemConferenciaResponse>();
        foreach (var item in nota.Itens)
        {
            var avisosItem = new List<string>();
            Produto? produto = null;
            string? comoAchou = null;
            var (fator, fatorDaNota) = FatorPelaNota(item);

            // 1º o vínculo (alguém já disse "esse item é aquele produto"); 2º o código de barras da unidade; 3º o da embalagem.
            if (vinculos.TryGetValue(item.CodigoFornecedor, out var vinculo) && porId.TryGetValue(vinculo.ProdutoId, out var vinculado))
            {
                (produto, comoAchou, fator, fatorDaNota) = (vinculado, "vínculo", vinculo.Fator, true);
            }
            else if (item.EanTributavel is not null && porCodigo.TryGetValue(item.EanTributavel, out var pelaUnidade))
            {
                (produto, comoAchou) = (pelaUnidade, "código de barras");
            }
            else if (item.Ean is not null && porCodigo.TryGetValue(item.Ean, out var pelaEmbalagem))
            {
                // O código de barras é o da EMBALAGEM que veio (ex.: o fardo): 1 da nota = 1 do produto.
                (produto, comoAchou, fator, fatorDaNota) = (pelaEmbalagem, "código de barras", 1, false);
            }

            if (produto is { Ativo: false })
                avisosItem.Add("O produto está desativado: reative-o antes de lançar (ou escolha outro).");
            if (produto is not null && item.CustoTotal > 0 && item.QuantidadeComercial * fator > 0)
            {
                var custoNovo = item.CustoTotal / (item.QuantidadeComercial * fator);
                if (produto.PrecoCusto > 0 && custoNovo > produto.PrecoCusto * 1.2m)
                    avisosItem.Add($"Custo subiu mais de 20% (era {Reais(produto.PrecoCusto)}, agora {Reais(custoNovo)}). Confira o fator e o preço de venda.");
                if (custoNovo >= produto.PrecoVenda)
                    avisosItem.Add($"O custo ({Reais(custoNovo)}) ficou igual ou maior que o preço de venda ({Reais(produto.PrecoVenda)}).");
            }

            var sugestao = Sugerir(item, fator, categorias, margens);
            if (produto is null && (nomes.TryGetValue(ImportacaoProdutosService.Normalizar(sugestao.Nome), out var mesmoNome)
                                    || nomes.TryGetValue(ImportacaoProdutosService.Normalizar(NomeBonito(item.Descricao)), out mesmoNome)))
                avisosItem.Add($"Já existe \"{mesmoNome}\" cadastrado. Se for o mesmo, escolha \"Produto cadastrado\" (assim não duplica).");

            var validade = item.Lotes.Where(l => l.Validade is not null).Select(l => l.Validade).Min();
            itens.Add(new ItemConferenciaResponse(
                item.NumeroItem, item.CodigoFornecedor, item.EanTributavel ?? item.Ean, item.Descricao, item.Ncm,
                item.UnidadeComercial, item.QuantidadeComercial, item.CustoTotal,
                produto is null ? null : new ProdutoConferenciaResponse(produto.Id, produto.Nome, produto.CodigoBarras,
                    produto.Unidade, produto.ControlaValidade, produto.Ativo, produto.PrecoCusto, produto.PrecoVenda),
                comoAchou, fator, fatorDaNota, validade, sugestao, avisosItem));
        }

        var pedidos = fornecedor is null ? [] : await db.PedidosCompra.AsNoTracking()
            .Where(p => p.FornecedorId == fornecedor.Id && p.Status == StatusPedido.Enviado)
            .OrderBy(p => p.PrevisaoEntrega)
            .Select(p => new PedidoAbertoResponse(p.Id, p.CriadoEm, p.PrevisaoEntrega, p.Itens.Count,
                p.Itens.Sum(i => i.Quantidade * i.CustoUnitario)))
            .ToListAsync();

        return new ConferenciaNotaResponse(nota.Chave, nota.Numero, nota.Serie, nota.DataEmissao, nota.ValorTotal,
            new FornecedorNotaResponse(fornecedor?.Id, fornecedor?.Nome ?? NomeDoFornecedor(nota), nota.EmitenteNome,
                FormatarCnpj(nota.EmitenteCnpj), fornecedor is null),
            bloqueio, avisos, pedidos, pedidos.Count == 1 ? pedidos[0].Id : null, itens,
            new PagamentoNotaResponse(nota.Duplicatas.Select(d => new ParcelaNotaResponse(d.Numero, d.Vencimento, d.Valor)).ToList(),
                nota.FormasPagamento));
    }

    // ===================================================================== gravação

    public async Task<RegistroNotaResponse> RegistrarAsync(byte[] xml, RegistrarNotaRequest pedido, string usuarioId)
    {
        var nota = Ler(xml);
        if (await db.NotasEntrada.AnyAsync(n => n.Chave == nota.Chave))
            throw new NotaEntradaException("Esta nota já foi lançada no estoque.");

        var decisoes = pedido.Itens.ToDictionary(d => d.NumeroItem);
        var faltando = nota.Itens.Where(i => !decisoes.ContainsKey(i.NumeroItem)).Select(i => i.NumeroItem).ToList();
        if (faltando.Count > 0)
            throw new NotaEntradaException($"Falta decidir o item {string.Join(", ", faltando)} da nota.");
        if (decisoes.Values.All(d => d.Acao == "ignorar"))
            throw new NotaEntradaException("Todos os itens foram ignorados: não há nada para lançar.");

        await using var transacao = await db.Database.BeginTransactionAsync();

        // 1) Fornecedor: pelo CNPJ; se não existe, cadastra com os dados da nota.
        var fornecedor = await AcharFornecedorAsync(nota.EmitenteCnpj);
        var fornecedorCriado = fornecedor is null;
        if (fornecedor is null)
        {
            var nome = Cortar(NomeDoFornecedor(nota), 120);
            if (await db.Fornecedores.AnyAsync(f => f.Nome == nome))
                nome = Cortar($"{nome} ({FormatarCnpj(nota.EmitenteCnpj)})", 120);
            fornecedor = new Fornecedor { Nome = nome, Cnpj = FormatarCnpj(nota.EmitenteCnpj), Observacao = "Cadastrado pela NF-e " + nota.Numero };
            db.Fornecedores.Add(fornecedor);
            await db.SaveChangesAsync();
        }

        // 2) Produtos novos.
        var categorias = await db.Categorias.Select(c => c.Id).ToListAsync();
        var produtoDoItem = new Dictionary<int, int>();
        var criados = 0;
        foreach (var item in nota.Itens)
        {
            var d = decisoes[item.NumeroItem];
            switch (d.Acao)
            {
                case "ignorar":
                    continue;
                case "existente":
                    produtoDoItem[item.NumeroItem] = d.ProdutoId ?? throw Erro(item, "escolha o produto.");
                    break;
                case "novo":
                    var novo = d.Novo ?? throw Erro(item, "faltam os dados do produto novo.");
                    var produto = await CriarProdutoAsync(item, novo, categorias, fornecedor.Id);
                    produtoDoItem[item.NumeroItem] = produto.Id;
                    criados++;
                    break;
                default:
                    throw Erro(item, $"ação \"{d.Acao}\" desconhecida.");
            }
        }

        // 3) Entrada no estoque (com as regras de sempre: validade, lote, custo) + vínculos.
        var observacao = Cortar($"NF-e {nota.Numero} série {nota.Serie} — {fornecedor.Nome}", 300);
        var recebido = new Dictionary<int, (decimal Quantidade, DateOnly? Validade, decimal Custo)>();
        foreach (var item in nota.Itens.Where(i => produtoDoItem.ContainsKey(i.NumeroItem)))
        {
            var d = decisoes[item.NumeroItem];
            var produtoId = produtoDoItem[item.NumeroItem];
            if (d.Fator is <= 0 or > 100000) throw Erro(item, "o fator (unidades por embalagem) deve ser maior que zero.");

            var quantidade = item.QuantidadeComercial * d.Fator;
            var custo = quantidade > 0 ? decimal.Round(item.CustoTotal / quantidade, 2) : 0;
            try
            {
                await estoque.RegistrarEntradaAsync(produtoId, quantidade, custo > 0 ? custo : null, d.Validade, observacao, usuarioId);
            }
            catch (EstoqueException e)
            {
                throw Erro(item, e.Message);
            }

            var p = await db.Produtos.FirstAsync(x => x.Id == produtoId);
            p.FornecedorId ??= fornecedor.Id; // a sugestão de compra passa a saber de quem comprar
            p.Ncm ??= NcmValido(item.Ncm);    // dados fiscais da nota do fornecedor (prepara a NFC-e)
            p.Cest ??= CestValido(item.Cest);

            if (!string.IsNullOrWhiteSpace(item.CodigoFornecedor))
            {
                var codigo = Cortar(item.CodigoFornecedor, 60);
                var vinculo = await db.VinculosFornecedor.FirstOrDefaultAsync(v => v.FornecedorId == fornecedor.Id && v.CodigoFornecedor == codigo);
                if (vinculo is null)
                    db.VinculosFornecedor.Add(vinculo = new VinculoFornecedorProduto { FornecedorId = fornecedor.Id, CodigoFornecedor = codigo });
                vinculo.ProdutoId = produtoId;
                vinculo.Fator = d.Fator;
                vinculo.AtualizadoEm = DateTimeOffset.UtcNow;
            }

            var anterior = recebido.GetValueOrDefault(produtoId);
            recebido[produtoId] = (anterior.Quantidade + quantidade, d.Validade ?? anterior.Validade, custo);
        }

        // 4) Pedido de compra (opcional): marca como recebido com o que veio na nota (sem lançar estoque de novo).
        int? pedidoRecebido = null;
        if (pedido.PedidoCompraId is int pedidoId)
        {
            var compra = await db.PedidosCompra.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId)
                ?? throw new NotaEntradaException("Pedido de compra não encontrado.");
            if (compra.FornecedorId != fornecedor.Id)
                throw new NotaEntradaException("O pedido escolhido é de outro fornecedor.");
            if (compra.Status != StatusPedido.Enviado)
                throw new NotaEntradaException($"O pedido #{compra.Id} não está aguardando entrega.");
            foreach (var i in compra.Itens)
            {
                var r = recebido.GetValueOrDefault(i.ProdutoId);
                i.QuantidadeRecebida = r.Quantidade;
                i.Validade = r.Validade;
                if (r.Custo > 0) i.CustoUnitario = r.Custo;
            }
            compra.Status = StatusPedido.Recebido;
            compra.RecebidoEm = DateTimeOffset.UtcNow;
            compra.RecebidoPorId = usuarioId;
            pedidoRecebido = compra.Id;
        }

        // 5) A nota (com o XML guardado).
        var registro = new NotaEntrada
        {
            Chave = nota.Chave, Numero = Cortar(nota.Numero, 9), Serie = Cortar(nota.Serie, 3), DataEmissao = nota.DataEmissao.ToUniversalTime(), // o Npgsql só grava em UTC
            ValorTotal = nota.ValorTotal, QuantidadeItens = nota.Itens.Count, FornecedorId = fornecedor.Id,
            PedidoCompraId = pedidoRecebido, Xml = Encoding.UTF8.GetString(xml).TrimStart('﻿'), RegistradaPorId = usuarioId,
        };
        db.NotasEntrada.Add(registro);

        // 6) Contas a pagar: uma por parcela (duplicata); sem parcelas, uma só com o total (à vista).
        var contas = new List<ContaPagar>();
        if (pedido.GerarContas && nota.ValorTotal > 0)
        {
            var titulo = Cortar($"NF-e {nota.Numero} — {fornecedor.Nome}", 170);
            if (nota.Duplicatas.Count > 0)
            {
                var n = nota.Duplicatas.Count;
                contas.AddRange(nota.Duplicatas.Select((d, i) => new ContaPagar
                {
                    Descricao = n > 1 ? $"{titulo} · parcela {i + 1}/{n}" : titulo,
                    Documento = Cortar(d.Numero ?? $"{nota.Numero}/{i + 1}", 60), Vencimento = d.Vencimento, Valor = d.Valor,
                }));
            }
            else
            {
                var emissao = Relogio.DiaBrasilia(nota.DataEmissao);
                var conta = new ContaPagar { Descricao = titulo + " · à vista", Documento = nota.Numero, Vencimento = emissao, Valor = nota.ValorTotal };
                if (pedido.JaPaga)
                {
                    conta.PagaEm = emissao;
                    conta.ValorPago = nota.ValorTotal;
                    conta.FormaPagamento = nota.FormasPagamento.FirstOrDefault();
                    conta.PagaPorId = usuarioId;
                }
                contas.Add(conta);
            }
            foreach (var c in contas)
            {
                c.Fornecedor = fornecedor;
                c.NotaEntrada = registro;
                c.CriadaPorId = usuarioId;
            }
            db.ContasPagar.AddRange(contas);
        }

        try
        {
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Duas pessoas lançando a mesma nota ao mesmo tempo: a chave única do banco barra a segunda.
            throw new NotaEntradaException("Esta nota acabou de ser lançada por outra pessoa. Confira o histórico.");
        }

        return new RegistroNotaResponse(registro.Id, produtoDoItem.Count, nota.Itens.Count - produtoDoItem.Count, criados,
            fornecedorCriado, fornecedor.Nome, pedidoRecebido, contas.Count);
    }

    // ===================================================================== histórico

    public Task<List<NotaEntradaResponse>> ListarAsync(int quantidade) =>
        db.NotasEntrada.AsNoTracking().OrderByDescending(n => n.RegistradaEm).Take(quantidade)
            .Select(n => new NotaEntradaResponse(n.Id, n.Chave, n.Numero, n.Serie, n.DataEmissao, n.ValorTotal, n.QuantidadeItens,
                n.Fornecedor!.Nome, n.PedidoCompraId, n.RegistradaEm, n.RegistradaPor!.NomeCompleto))
            .ToListAsync();

    public async Task<(string Nome, string Xml)?> XmlAsync(int id) =>
        await db.NotasEntrada.AsNoTracking().Where(n => n.Id == id)
            .Select(n => new { n.Chave, n.Xml }).FirstOrDefaultAsync() is { } n ? ($"NFe{n.Chave}.xml", n.Xml) : null;

    // ===================================================================== apoio

    private static LeitorNfe.Nota Ler(byte[] xml)
    {
        try { return LeitorNfe.Ler(new MemoryStream(xml)); }
        catch (LeitorNfe.NotaInvalidaException e) { throw new NotaEntradaException(e.Message); }
    }

    private static NotaEntradaException Erro(LeitorNfe.Item item, string mensagem) =>
        new($"Item {item.NumeroItem} ({Cortar(item.Descricao, 40)}): {mensagem}");

    private async Task<Produto> CriarProdutoAsync(LeitorNfe.Item item, NovoProdutoNota novo, List<int> categorias, int fornecedorId)
    {
        var nome = novo.Nome?.Trim() ?? "";
        if (nome.Length is < 2 or > 150) throw Erro(item, "o nome do produto novo deve ter de 2 a 150 letras.");
        if (!categorias.Contains(novo.CategoriaId)) throw Erro(item, "escolha a categoria do produto novo.");
        if (!Unidades.Todas.Contains(novo.Unidade)) throw Erro(item, "unidade inválida.");
        if (novo.PrecoVenda is <= 0 or > 999999) throw Erro(item, "informe o preço de venda do produto novo.");
        var codigo = string.IsNullOrWhiteSpace(novo.CodigoBarras) ? null : novo.CodigoBarras.Trim();
        if (codigo is not null && !Regex.IsMatch(codigo, @"^\d{8,14}$")) throw Erro(item, "código de barras inválido.");
        if (codigo is not null && await db.Produtos.AnyAsync(p => p.CodigoBarras == codigo))
            throw Erro(item, $"já existe um produto com o código de barras {codigo}: escolha-o em vez de cadastrar.");

        var produto = new Produto
        {
            Nome = nome, CodigoBarras = codigo, CategoriaId = novo.CategoriaId, Unidade = novo.Unidade,
            PrecoVenda = decimal.Round(novo.PrecoVenda, 2), ControlaValidade = novo.ControlaValidade, FornecedorId = fornecedorId,
            Ncm = NcmValido(item.Ncm), Cest = CestValido(item.Cest),
        };
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
        return produto;
    }

    private async Task<Fornecedor?> AcharFornecedorAsync(string cnpj)
    {
        // O CNPJ pode ter sido digitado com ou sem pontuação no cadastro: compara só os números.
        var comCnpj = await db.Fornecedores.Where(f => f.Cnpj != null).ToListAsync();
        return comCnpj.FirstOrDefault(f => SoNumeros(f.Cnpj!) == cnpj);
    }

    // Unidades por embalagem pela própria nota: "2 CX" com quantidade tributável "24 UN" → 12 por caixa.
    private static (decimal Fator, bool DaNota) FatorPelaNota(LeitorNfe.Item item)
    {
        if (item.QuantidadeTributavel is decimal qTrib && item.QuantidadeComercial > 0
            && item.UnidadeTributavel is not null && item.UnidadeTributavel != item.UnidadeComercial)
        {
            var fator = qTrib / item.QuantidadeComercial;
            if (fator > 1 && fator % 1 == 0) return (fator, true);
        }
        return (1, false);
    }

    private static NovoProdutoSugestao Sugerir(LeitorNfe.Item item, decimal fator, List<Categoria> categorias,
        Dictionary<int, decimal> margens)
    {
        var unidade = Unidade(fator > 1 ? item.UnidadeTributavel ?? "UN" : item.UnidadeComercial);
        var nomeCategoria = CategoriaPeloNcm(item.Ncm);
        var categoria = categorias.FirstOrDefault(c => ImportacaoProdutosService.Normalizar(c.Nome) == ImportacaoProdutosService.Normalizar(nomeCategoria))
                        ?? categorias.FirstOrDefault(c => ImportacaoProdutosService.Normalizar(c.Nome) == "mercearia");
        var perecivel = nomeCategoria is "Hortifrúti" or "Frios e Laticínios" or "Padaria" or "Açougue" || item.Lotes.Count > 0;

        decimal? preco = null;
        var quantidade = item.QuantidadeComercial * fator;
        if (quantidade > 0 && item.CustoTotal > 0)
        {
            var margem = categoria is not null && margens.TryGetValue(categoria.Id, out var m) ? m : 1.35m;
            preco = PrecoRedondo(item.CustoTotal / quantidade * margem);
        }

        // Só sugere como código de barras do produto o da UNIDADE (o da caixa não é o que passa no caixa).
        var codigo = fator > 1 ? item.EanTributavel : item.EanTributavel ?? item.Ean;
        var nome = NomeBonito(item.Descricao);
        if (unidade == Unidades.Quilo && nome.EndsWith(" Kg") && nome.Length > 5) nome = nome[..^3]; // "Banana Prata Kg" → "Banana Prata"
        return new NovoProdutoSugestao(nome, codigo, categoria?.Id, unidade, perecivel, preco);
    }

    // Margem média (venda ÷ custo) de cada categoria, para sugerir o preço de venda de produto novo.
    private async Task<Dictionary<int, decimal>> MargensPorCategoriaAsync() =>
        (await db.Produtos.AsNoTracking().Where(p => p.Ativo && p.PrecoCusto > 0)
            .GroupBy(p => p.CategoriaId)
            .Select(g => new { g.Key, Margem = g.Average(p => p.PrecoVenda / p.PrecoCusto) })
            .ToListAsync())
        .ToDictionary(x => x.Key, x => Math.Clamp(x.Margem, 1.05m, 3m));

    // 4,13 → 4,19 · 12,52 → 12,59 (o "preço de mercado", terminado em 9).
    private static decimal PrecoRedondo(decimal valor) => Math.Max(Math.Ceiling(valor * 10) / 10 - 0.01m, 0.09m);

    private static string Unidade(string unidadeDaNota) => unidadeDaNota.ToUpperInvariant() switch
    {
        "KG" or "KGS" or "QUILO" => Unidades.Quilo,
        "L" or "LT" or "LITRO" => Unidades.Litro,
        "PCT" or "PC" or "PACOTE" => Unidades.Pacote,
        "DZ" or "DUZIA" => Unidades.Duzia,
        _ => Unidades.Unidade, // UN, UND, UNID, CX, FD... (caixa/fardo viram unidade com o fator)
    };

    // Pelo NCM (classificação fiscal do produto), um palpite de categoria. A pessoa confirma na tela.
    private static string CategoriaPeloNcm(string? ncm) => ncm switch
    {
        null => "Mercearia",
        _ when ncm.StartsWith("02") || ncm.StartsWith("1601") || ncm.StartsWith("1602") => "Açougue",
        _ when ncm.StartsWith("0401") || ncm.StartsWith("0402") || ncm.StartsWith("0403") || ncm.StartsWith("0405") || ncm.StartsWith("0406") => "Frios e Laticínios",
        _ when ncm.StartsWith("07") || ncm.StartsWith("08") => "Hortifrúti",
        _ when ncm.StartsWith("190590") || ncm.StartsWith("190520") => "Padaria",
        _ when ncm.StartsWith("22") || ncm.StartsWith("2009") => "Bebidas",
        _ when ncm.StartsWith("3402") || ncm.StartsWith("3808") || ncm.StartsWith("3405") => "Limpeza",
        _ when ncm.StartsWith("3401") || ncm.StartsWith("3303") || ncm.StartsWith("3304") || ncm.StartsWith("3305")
               || ncm.StartsWith("3306") || ncm.StartsWith("3307") || ncm.StartsWith("4818") || ncm.StartsWith("9619") => "Higiene e Beleza",
        _ => "Mercearia",
    };

    // "REFRIG COLA BOM DIA 2L FD C/6" → "Refrig Cola Bom Dia 2l Fd C/6"; mantém "500ml", "5kg" em minúsculas.
    private static string NomeBonito(string descricao)
    {
        string[] pequenas = ["de", "da", "do", "das", "dos", "e", "com", "c/", "em", "para"];
        // Tira a embalagem do fornecedor do fim do nome ("CX C/30", "FD C/6", "PCT 10X1KG"): no produto vale a unidade.
        descricao = Regex.Replace(descricao, @"\s+(CX|FD|FDO|PCT|PC|DP|BD)\s*(C/|COM\s)?\s*\d+\s*(UN|UND|X\S*)?\s*$", "", RegexOptions.IgnoreCase);
        var palavras = descricao.Trim().ToLower(PtBr).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i > 0 && pequenas.Contains(p) || char.IsDigit(p[0]) ? p : char.ToUpper(p[0]) + p[1..]);
        return Cortar(string.Join(' ', palavras), 150);
    }

    private static string? NcmValido(string? ncm) => ncm is not null && Regex.IsMatch(ncm, @"^\d{8}$") && ncm != "00000000" ? ncm : null;
    private static string? CestValido(string? cest) => cest is not null && Regex.IsMatch(cest, @"^\d{7}$") ? cest : null;

    private static string NomeDoFornecedor(LeitorNfe.Nota nota) =>
        string.IsNullOrWhiteSpace(nota.EmitenteFantasia) ? nota.EmitenteNome : nota.EmitenteFantasia;

    private static readonly CultureInfo PtBr = new("pt-BR");
    private static string Reais(decimal valor) => valor.ToString("C", PtBr);

    private static string SoNumeros(string texto) => new(texto.Where(char.IsDigit).ToArray());

    private static string FormatarCnpj(string cnpj) => cnpj.Length == 14
        ? $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}" : cnpj;

    private static string Cortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
