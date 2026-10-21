using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Marketplace.Api.Contracts;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;
using static Marketplace.Api.Services.LeitorPlanilha;

namespace Marketplace.Api.Services;

// Passo 2: lê a planilha enviada, interpreta e confere cada linha e diz o que ACONTECERIA (prévia).
// Nada é gravado aqui. A gravação (passo 4) vai usar a mesma análise, para a prévia e a gravação nunca discordarem.
public partial class ImportacaoProdutosService
{
    public const int MaxLinhas = 5000;

    // Posição de cada campo no array Colunas (e no array de valores de cada linha).
    private const int CodigoBarras = 0, Nome = 1, Categoria = 2, Unidade = 3, PrecoCusto = 4, PrecoVenda = 5,
        EstoqueMinimo = 6, ControlaValidade = 7, EstoqueInicial = 8, Validade = 9, Ncm = 10, Fornecedor = 11, PrazoFornecedor = 12,
        CodigoBalanca = 13, ModeloCol = 14, TamanhoCol = 15, CorCol = 16, MarcaCol = 17, ServicoCol = 18;

    // Outros nomes aceitos no cabeçalho (planilha exportada de outro sistema). Comparados sem acento e sem maiúsculas.
    private static readonly Dictionary<string, int> NomesDasColunas = new()
    {
        ["codigo de barras"] = CodigoBarras, ["codigo"] = CodigoBarras, ["ean"] = CodigoBarras, ["gtin"] = CodigoBarras,
        ["cod barras"] = CodigoBarras, ["cod. barras"] = CodigoBarras,
        ["nome"] = Nome, ["produto"] = Nome, ["descricao"] = Nome, ["nome do produto"] = Nome,
        ["categoria"] = Categoria, ["grupo"] = Categoria, ["departamento"] = Categoria,
        ["unidade"] = Unidade, ["un"] = Unidade, ["und"] = Unidade, ["unid"] = Unidade, ["unidade de medida"] = Unidade,
        ["preco de custo"] = PrecoCusto, ["custo"] = PrecoCusto, ["preco custo"] = PrecoCusto,
        ["preco de venda"] = PrecoVenda, ["preco"] = PrecoVenda, ["venda"] = PrecoVenda, ["preco venda"] = PrecoVenda,
        ["estoque minimo"] = EstoqueMinimo, ["minimo"] = EstoqueMinimo, ["estoque min"] = EstoqueMinimo,
        ["controla validade"] = ControlaValidade, ["tem validade"] = ControlaValidade, ["perecivel"] = ControlaValidade,
        ["estoque inicial"] = EstoqueInicial, ["estoque"] = EstoqueInicial, ["quantidade"] = EstoqueInicial,
        ["qtd"] = EstoqueInicial, ["estoque atual"] = EstoqueInicial,
        ["validade"] = Validade, ["data de validade"] = Validade, ["vencimento"] = Validade,
        ["ncm"] = Ncm, ["classificacao fiscal"] = Ncm, ["cod ncm"] = Ncm,
        ["fornecedor"] = Fornecedor, ["distribuidor"] = Fornecedor, ["fornecedor principal"] = Fornecedor,
        ["prazo de entrega (dias)"] = PrazoFornecedor, ["prazo de entrega"] = PrazoFornecedor, ["prazo"] = PrazoFornecedor,
        ["prazo do fornecedor"] = PrazoFornecedor,
        ["codigo na balanca (plu)"] = CodigoBalanca, ["codigo na balanca"] = CodigoBalanca, ["codigo da balanca"] = CodigoBalanca,
        ["codigo balanca"] = CodigoBalanca, ["cod balanca"] = CodigoBalanca, ["plu"] = CodigoBalanca, ["balanca"] = CodigoBalanca,
        ["modelo"] = ModeloCol, ["grade"] = ModeloCol, ["produto pai"] = ModeloCol,
        ["tamanho"] = TamanhoCol, ["tam"] = TamanhoCol, ["numeracao"] = TamanhoCol, ["numero"] = TamanhoCol,
        ["cor"] = CorCol, ["marca"] = MarcaCol, ["servico"] = ServicoCol,
    };

    // O que a análise descobriu sobre uma linha (a prévia mostra; a gravação usa).
    internal class LinhaAnalisada
    {
        public int Linha { get; init; }
        public string? CodigoBarras { get; set; }
        public string? Nome { get; set; }
        public string? Categoria { get; set; }
        public string? Unidade { get; set; }
        public decimal? PrecoCusto { get; set; }
        public decimal? PrecoVenda { get; set; }
        public decimal? EstoqueMinimo { get; set; }
        public bool? ControlaValidade { get; set; }
        public decimal? EstoqueInicial { get; set; }
        public DateOnly? Validade { get; set; }
        public string? Ncm { get; set; }
        public string? Fornecedor { get; set; }
        public int? CodigoBalanca { get; set; }
        public string? Modelo { get; set; }   // grade: nome do modelo (as linhas com o mesmo modelo viram um Modelo)
        public string? Tamanho { get; set; }
        public string? Cor { get; set; }
        public string? Marca { get; set; }
        public bool Servico { get; set; }
        public int? ProdutoExistenteId { get; set; } // preenchido = atualiza esse produto
        public List<string> Erros { get; } = [];
        public List<string> Avisos { get; } = [];
        public string Acao => Erros.Count > 0 ? "Erro" : ProdutoExistenteId is null ? "Novo" : "Atualizar";
    }

    internal record Analise(List<LinhaAnalisada> Linhas, List<string> CategoriasNovas, List<string> ErrosGerais, List<string> AvisosGerais,
        List<(string Nome, int Prazo)>? FornecedoresNovos = null);

    public async Task<PreviaImportacaoResponse> GerarPreviaAsync(Stream arquivo, string nomeDoArquivo)
    {
        var analise = await AnalisarAsync(arquivo, nomeDoArquivo);
        var linhas = analise.Linhas;
        return new(nomeDoArquivo, linhas.Count,
            linhas.Count(l => l.Acao == "Novo"), linhas.Count(l => l.Acao == "Atualizar"), linhas.Count(l => l.Acao == "Erro"),
            linhas.Count(l => l.Avisos.Count > 0), analise.CategoriasNovas, analise.ErrosGerais, analise.AvisosGerais,
            [.. (analise.FornecedoresNovos ?? []).Select(f => $"{f.Nome} (entrega em {f.Prazo} dia{(f.Prazo == 1 ? "" : "s")})")],
            linhas.Select(l => new LinhaPreviaResponse(l.Linha, l.Acao, l.CodigoBarras, l.Nome, l.Categoria, l.Unidade,
                l.PrecoCusto, l.PrecoVenda, l.EstoqueMinimo, l.ControlaValidade, l.EstoqueInicial, l.Validade,
                l.Erros, l.Avisos)).ToList());
    }

    internal async Task<Analise> AnalisarAsync(Stream arquivo, string nomeDoArquivo)
    {
        Resultado planilha;
        try { planilha = Ler(arquivo, nomeDoArquivo, AbaProdutos, MaxLinhas); }
        catch (PlanilhaInvalidaException e) { return new([], [], [e.Message], []); }

        // 1) Cabeçalho: descobre em que coluna está cada campo (a ordem pode ser outra).
        var posicao = new int?[Colunas.Length];
        var avisosGerais = new List<string>();
        for (var c = 0; c < planilha.Cabecalho.Length; c++)
        {
            var titulo = planilha.Cabecalho[c];
            if (string.IsNullOrWhiteSpace(titulo)) continue;
            if (NomesDasColunas.TryGetValue(Normalizar(titulo.Replace("*", "").Replace("(R$)", "")), out var campo) && posicao[campo] is null)
                posicao[campo] = c;
            else
                avisosGerais.Add($"A coluna \"{titulo}\" não foi reconhecida e será ignorada.");
        }
        var faltando = Colunas.Where((col, i) => col.Obrigatoria && posicao[i] is null).Select(col => $"\"{col.Titulo}\"").ToList();
        if (planilha.Linhas.Count == 0)
            return new([], [], ["A planilha está vazia: preencha os produtos a partir da linha 2 da aba \"Produtos\"."], avisosGerais);
        if (faltando.Count > 0)
            return new([], [], [$"Falta a coluna {string.Join(", ", faltando)} na linha 1. Use o modelo da planilha."], avisosGerais);

        // 2) O que já existe no banco (só desta empresa: o filtro global cuida disso).
        var produtos = await db.Produtos.AsNoTracking()
            .Select(p => new { p.Id, p.CodigoBarras, p.Nome, p.Unidade, p.ControlaValidade, p.Ativo, p.CodigoBalanca,
                Modelo = p.Modelo != null ? p.Modelo.Nome : null, p.Tamanho, p.Cor }).ToListAsync();
        var variacaoExistente = produtos.Where(p => p.Modelo != null)
            .GroupBy(p => (Normalizar(p.Modelo!), p.Tamanho ?? "", p.Cor ?? "")).ToDictionary(g => g.Key, g => g.First());
        var variacoesVistas = new Dictionary<(string, string, string), int>();
        var porCodigo = produtos.Where(p => p.CodigoBarras != null).ToDictionary(p => p.CodigoBarras!);
        var semCodigoPorNome = produtos.Where(p => p.CodigoBarras == null).ToLookup(p => Normalizar(p.Nome));
        var comCodigoPorNome = produtos.Where(p => p.CodigoBarras != null).ToLookup(p => Normalizar(p.Nome));
        var categorias = await db.Categorias.AsNoTracking().Select(c => new { c.Nome, c.Ativa }).ToListAsync();
        var categoriaPorNome = categorias.GroupBy(c => Normalizar(c.Nome)).ToDictionary(g => g.Key, g => g.First());
        var fornecedorPorNome = (await db.Fornecedores.AsNoTracking().Select(f => f.Nome).ToListAsync())
            .GroupBy(Normalizar).ToDictionary(g => g.Key, g => g.First());
        var fornecedoresNovos = new Dictionary<string, (string Nome, int Prazo)>();

        var hoje = Relogio.Hoje;
        var codigosVistos = new Dictionary<string, int>();
        var pluPorProduto = produtos.Where(p => p.CodigoBalanca != null).ToDictionary(p => p.CodigoBalanca!.Value);
        var plusVistos = new Dictionary<int, int>();
        var nomesSemCodigoVistos = new Dictionary<string, int>();
        var categoriasNovas = new Dictionary<string, string>(); // normalizado → como o cliente escreveu (a 1ª vez)
        var resultado = new List<LinhaAnalisada>();

        foreach (var linha in planilha.Linhas)
        {
            Celula Valor(int campo) => posicao[campo] is int c && c < linha.Celulas.Length ? linha.Celulas[c] : Celula.Vazia;
            var a = new LinhaAnalisada { Linha = linha.NumeroDaLinha };
            resultado.Add(a);

            // Código de barras: só números, 8 a 14 dígitos. Se veio como NÚMERO do Excel, sem casas decimais.
            var cod = Valor(CodigoBarras);
            if (!cod.EstaVazia)
            {
                var texto = cod.Numero is double n ? Math.Round(n).ToString("0", CultureInfo.InvariantCulture) : cod.Texto!.Trim();
                a.CodigoBarras = texto;
                if (texto.Contains('E', StringComparison.OrdinalIgnoreCase) || texto.Contains(','))
                    a.Erros.Add("Código de barras estragado pelo Excel (virou número científico). Formate a coluna como Texto e digite de novo.");
                else if (!Regex.IsMatch(texto, @"^\d{8,14}$"))
                    a.Erros.Add("Código de barras deve ter só números, de 8 a 14 dígitos.");
                else if (codigosVistos.TryGetValue(texto, out var outra))
                    a.Erros.Add($"Código de barras repetido (já está na linha {outra}).");
                else
                    codigosVistos[texto] = a.Linha;
            }

            // Grade: Modelo + Tamanho + Cor. O nome do cupom é montado ("Modelo Tamanho Cor").
            a.Modelo = Valor(ModeloCol).Texto?.Trim() is { Length: > 0 } mod ? mod : null;
            a.Tamanho = TextoDe(Valor(TamanhoCol));
            a.Cor = Valor(CorCol).Texto?.Trim() is { Length: > 0 } cor ? cor : null;
            a.Marca = Valor(MarcaCol).Texto?.Trim() is { Length: > 0 } marca ? marca : null;
            if (a.Modelo is not null)
            {
                if (a.Modelo.Length > 120) a.Erros.Add("O nome do modelo pode ter no máximo 120 letras.");
                if (a.Tamanho is null && a.Cor is null) a.Erros.Add("Linha de modelo (grade): informe o tamanho e/ou a cor.");
                a.Tamanho ??= Models.Modelo.TamanhoUnico;
                a.Cor ??= Models.Modelo.CorUnica;
                if (a.Tamanho.Length > 20) a.Erros.Add("Tamanho: no máximo 20 letras.");
                if (a.Cor.Length > 40) a.Erros.Add("Cor: no máximo 40 letras.");
                var chaveV = (Normalizar(a.Modelo), a.Tamanho, a.Cor);
                if (variacoesVistas.TryGetValue(chaveV, out var outraV)) a.Erros.Add($"Tamanho e cor repetidos neste modelo (linha {outraV}).");
                else variacoesVistas[chaveV] = a.Linha;
                if (variacaoExistente.TryGetValue(chaveV, out var jaTem) && jaTem.CodigoBarras is { } codJa
                    && a.CodigoBarras is not null && a.CodigoBarras != codJa)
                    a.Avisos.Add($"A variação já existe com o código {codJa}: o código será trocado.");
            }
            else if (a.Tamanho is not null || a.Cor is not null)
                a.Avisos.Add("Tamanho/cor sem Modelo: ignorados (produto simples).");
            a.Servico = LerSimNao(Valor(ServicoCol), a.Erros, "Serviço") ?? false;

            a.Nome = a.Modelo is not null ? Models.Modelo.NomeDaVariacao(a.Modelo, a.Tamanho, a.Cor) : Valor(Nome).Texto?.Trim();
            if (string.IsNullOrWhiteSpace(a.Nome)) a.Erros.Add("Falta o nome.");
            else if (a.Nome.Length is < 2 or > 150) a.Erros.Add("O nome deve ter de 2 a 150 letras.");

            a.Categoria = Valor(Categoria).Texto?.Trim();
            if (string.IsNullOrWhiteSpace(a.Categoria)) a.Erros.Add("Falta a categoria.");
            else if (a.Categoria.Length > 60) a.Erros.Add("O nome da categoria pode ter no máximo 60 letras.");
            else if (categoriaPorNome.TryGetValue(Normalizar(a.Categoria), out var existente))
            {
                a.Categoria = existente.Nome; // "hortifruti" vira "Hortifrúti", como está cadastrada
                if (!existente.Ativa) a.Avisos.Add($"A categoria \"{existente.Nome}\" está desativada.");
            }
            else
            {
                var chave = Normalizar(a.Categoria);
                if (categoriasNovas.TryGetValue(chave, out var comoEscreveu)) a.Categoria = comoEscreveu;
                else categoriasNovas[chave] = a.Categoria;
                a.Avisos.Add($"Categoria nova: \"{a.Categoria}\" será criada.");
            }

            var unidade = Valor(Unidade).Texto?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(unidade)) a.Erros.Add("Falta a unidade.");
            else if (!Unidades.Todas.Contains(unidade)) a.Erros.Add($"Unidade \"{unidade}\" não existe. Use: {string.Join(", ", Unidades.Todas)}.");
            else a.Unidade = unidade;

            a.PrecoCusto = LerDinheiro(Valor(PrecoCusto), "Preço de custo", a.Erros, podeSerZero: true);
            a.PrecoVenda = LerDinheiro(Valor(PrecoVenda), "Preço de venda", a.Erros, podeSerZero: false);
            if (Valor(PrecoVenda).EstaVazia) a.Erros.Add("Falta o preço de venda.");
            if (a.PrecoCusto > 0 && a.PrecoVenda > 0 && a.PrecoVenda < a.PrecoCusto)
                a.Avisos.Add("Preço de venda menor que o custo (vende no prejuízo).");

            a.EstoqueMinimo = LerQuantidade(Valor(EstoqueMinimo), "Estoque mínimo", a.Unidade, a.Erros);
            a.ControlaValidade = LerSimNao(Valor(ControlaValidade), a.Erros, "Controla validade");
            a.EstoqueInicial = LerQuantidade(Valor(EstoqueInicial), "Estoque inicial", a.Unidade, a.Erros);
            a.Validade = LerData(Valor(Validade), a.Erros);

            var ncm = Valor(Ncm);
            if (!ncm.EstaVazia)
            {
                var texto = ncm.Numero is double nn ? Math.Round(nn).ToString("00000000", CultureInfo.InvariantCulture) : ncm.Texto!.Replace(".", "").Trim();
                if (Regex.IsMatch(texto, @"^\d{8}$")) a.Ncm = texto;
                else a.Erros.Add("NCM deve ter 8 dígitos (ex.: 10063021).");
            }

            // Fornecedor (opcional): o que já existe (pelo nome, sem acento/maiúscula) ou um novo, criado na importação.
            a.Fornecedor = Valor(Fornecedor).Texto?.Trim();
            if (string.IsNullOrWhiteSpace(a.Fornecedor)) a.Fornecedor = null;
            else if (a.Fornecedor.Length > 120) a.Erros.Add("O nome do fornecedor pode ter no máximo 120 letras.");
            else if (fornecedorPorNome.TryGetValue(Normalizar(a.Fornecedor), out var fornecedorExistente)) a.Fornecedor = fornecedorExistente;
            else
            {
                int? prazo = null;
                try { prazo = LerNumero(Valor(PrazoFornecedor)) is decimal pz ? (int)pz : null; }
                catch (FormatException) { a.Erros.Add("Prazo de entrega: use um número de dias."); }
                if (prazo is < 0 or > 60) { a.Erros.Add("Prazo de entrega: de 0 a 60 dias."); prazo = null; }
                var chaveF = Normalizar(a.Fornecedor);
                if (fornecedoresNovos.TryGetValue(chaveF, out var jaVisto)) a.Fornecedor = jaVisto.Nome;
                else fornecedoresNovos[chaveF] = (a.Fornecedor, prazo ?? 2);
                // (sem aviso por linha: a prévia já lista os fornecedores novos no resumo)
            }

            if (a.Validade is DateOnly v)
            {
                if (a.ControlaValidade != true) a.Avisos.Add("Tem validade, mas \"Controla validade\" não é Sim: a data será ignorada.");
                else if (v < hoje) a.Erros.Add($"Validade {v:dd/MM/yyyy} já passou: não dá para lançar estoque vencido.");
                else if (v > hoje.AddYears(10)) a.Erros.Add($"Validade {v:dd/MM/yyyy} parece errada (mais de 10 anos).");
            }
            if (a.ControlaValidade == true && a.EstoqueInicial > 0 && a.Validade is null && Valor(Validade).EstaVazia)
                a.Erros.Add("Controla validade e tem estoque inicial: informe a validade.");

            // Produto já existe? Variação da grade: pelo modelo + tamanho + cor. Senão, pelo código de barras;
            // sem código, pelo nome (só entre os que também não têm código).
            if (a.Modelo is not null && variacaoExistente.TryGetValue((Normalizar(a.Modelo), a.Tamanho!, a.Cor!), out var mesmaVariacao))
            {
                a.ProdutoExistenteId = mesmaVariacao.Id;
                if (a.CodigoBarras is not null && porCodigo.TryGetValue(a.CodigoBarras, out var donoCodigo) && donoCodigo.Id != mesmaVariacao.Id)
                    a.Erros.Add($"O código {a.CodigoBarras} já é do produto \"{donoCodigo.Nome}\".");
            }
            else if (a.Modelo is not null)
            {
                if (a.CodigoBarras is not null && porCodigo.TryGetValue(a.CodigoBarras, out var donoCodigo))
                    a.Erros.Add($"O código {a.CodigoBarras} já é do produto \"{donoCodigo.Nome}\".");
            }
            else if (a.CodigoBarras is not null && porCodigo.TryGetValue(a.CodigoBarras, out var mesmoCodigo))
            {
                a.ProdutoExistenteId = mesmoCodigo.Id;
                if (!Normalizar(mesmoCodigo.Nome).Equals(Normalizar(a.Nome ?? "")))
                    a.Avisos.Add($"Já existe com este código como \"{mesmoCodigo.Nome}\": o nome será trocado.");
                if (!mesmoCodigo.Ativo) a.Avisos.Add("Este produto está desativado e será reativado.");
                if (a.Unidade is not null && a.Unidade != mesmoCodigo.Unidade)
                    a.Avisos.Add($"A unidade muda de {mesmoCodigo.Unidade} para {a.Unidade}.");
                if (a.ControlaValidade == false && mesmoCodigo.ControlaValidade)
                    a.Avisos.Add("O produto deixa de controlar validade (os lotes atuais continuam guardados).");
            }
            else if (a.CodigoBarras is null && !string.IsNullOrWhiteSpace(a.Nome))
            {
                var chave = Normalizar(a.Nome);
                if (nomesSemCodigoVistos.TryGetValue(chave, out var outra))
                    a.Erros.Add($"Produto sem código de barras repetido (mesmo nome da linha {outra}).");
                else
                    nomesSemCodigoVistos[chave] = a.Linha;

                var mesmos = semCodigoPorNome[chave].ToList();
                if (mesmos.Count > 1) a.Erros.Add("Há mais de um produto cadastrado com este nome e sem código: ajuste pela tela de Produtos.");
                else if (mesmos.Count == 1)
                {
                    a.ProdutoExistenteId = mesmos[0].Id;
                    if (!mesmos[0].Ativo) a.Avisos.Add("Este produto está desativado e será reativado.");
                }
            }
            if (a.ProdutoExistenteId is null && a.CodigoBarras is not null && !string.IsNullOrWhiteSpace(a.Nome)
                && comCodigoPorNome[Normalizar(a.Nome)].FirstOrDefault() is { } parecido)
                a.Avisos.Add($"Já existe um produto com este nome (código {parecido.CodigoBarras}): confira se não é o mesmo.");

            // Código na balança (PLU): inteiro de 1 a 99999, sem repetir na planilha nem com OUTRO produto já cadastrado.
            var plu = Valor(CodigoBalanca);
            if (!plu.EstaVazia)
            {
                decimal? numero = null;
                try { numero = LerNumero(plu); } catch (FormatException) { }
                if (numero is not decimal pl || pl % 1 != 0 || pl is < 1 or > 99999)
                    a.Erros.Add("Código na balança (PLU): use um número inteiro de 1 a 99999.");
                else if (plusVistos.TryGetValue((int)pl, out var outraLinha))
                    a.Erros.Add($"Código na balança {(int)pl} repetido (já está na linha {outraLinha}).");
                else
                {
                    plusVistos[(int)pl] = a.Linha;
                    a.CodigoBalanca = (int)pl;
                    if (pluPorProduto.TryGetValue((int)pl, out var dono) && dono.Id != a.ProdutoExistenteId)
                        a.Erros.Add($"Código na balança {(int)pl} já é do produto \"{dono.Nome}\".");
                }
            }

            if (a.ProdutoExistenteId is not null && a.EstoqueInicial > 0)
                a.Avisos.Add("Produto já cadastrado: o estoque inicial será ignorado (para corrigir, use a tela de Estoque).");
            if (a.Servico && a.EstoqueInicial > 0)
                a.Avisos.Add("Serviço não tem estoque: o estoque inicial será ignorado.");
            if (a.Servico && a.ControlaValidade == true)
                a.Erros.Add("Serviço não controla validade.");
        }

        // Só conta como categoria nova se alguma linha SEM erro precisar dela.
        var usadas = resultado.Where(l => l.Erros.Count == 0).Select(l => Normalizar(l.Categoria ?? "")).ToHashSet();
        var novas = categoriasNovas.Where(c => usadas.Contains(c.Key)).Select(c => c.Value).Order().ToList();
        var fornecedoresUsados = resultado.Where(l => l.Erros.Count == 0 && l.Fornecedor is not null).Select(l => Normalizar(l.Fornecedor!)).ToHashSet();
        var fornecedores = fornecedoresNovos.Where(f => fornecedoresUsados.Contains(f.Key)).Select(f => f.Value).OrderBy(f => f.Nome).ToList();
        return new(resultado, novas, [], avisosGerais, fornecedores);
    }

    // Tamanho pode vir como número do Excel (42 → "42", não "42.0").
    private static string? TextoDe(Celula c) =>
        c.EstaVazia ? null : c.Numero is double n ? n.ToString("0.##", CultureInfo.InvariantCulture) : c.Texto!.Trim() is { Length: > 0 } t ? t : null;

    // "Hortifrúti " → "hortifruti" (para comparar nomes sem ligar para acento, maiúscula e espaços).
    internal static string Normalizar(string texto)
    {
        var semAcento = new StringBuilder();
        foreach (var c in texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) semAcento.Append(c);
        return Regex.Replace(semAcento.ToString(), @"\s+", " ");
    }

    // Aceita número do Excel ou texto: "29,90", "R$ 1.234,56", "1234.56", "1.500" (= mil e quinhentos).
    internal static decimal? LerNumero(Celula celula)
    {
        if (celula.EstaVazia) return null;
        if (celula.Numero is double n) return (decimal)n;
        var t = Regex.Replace(celula.Texto!, @"[R$\s]", "");
        if (t.Contains(',') && t.Contains('.'))
            t = t.LastIndexOf(',') > t.LastIndexOf('.') ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (t.Contains(','))
            t = t.Replace(',', '.');
        else if (Regex.IsMatch(t, @"^-?\d{1,3}(\.\d{3})+$"))
            t = t.Replace(".", "");
        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)
            ? v : throw new FormatException();
    }

    private static decimal? LerDinheiro(Celula celula, string campo, List<string> erros, bool podeSerZero)
    {
        decimal? valor;
        try { valor = LerNumero(celula); }
        catch (FormatException) { erros.Add($"{campo}: \"{celula.Texto}\" não é um valor válido."); return null; }
        if (valor is null) return null;
        if (valor < 0 || valor > 999999 || (!podeSerZero && valor == 0))
        {
            erros.Add(podeSerZero ? $"{campo} inválido." : $"{campo} deve ser maior que zero.");
            return null;
        }
        if (decimal.Round(valor.Value, 2) != valor) erros.Add($"{campo} com mais de 2 casas decimais.");
        return decimal.Round(valor.Value, 2);
    }

    private static decimal? LerQuantidade(Celula celula, string campo, string? unidade, List<string> erros)
    {
        decimal? valor;
        try { valor = LerNumero(celula); }
        catch (FormatException) { erros.Add($"{campo}: \"{celula.Texto}\" não é um número válido."); return null; }
        if (valor is null) return null;
        if (valor < 0 || valor > 999999) { erros.Add($"{campo} inválido."); return null; }
        var fracionado = unidade is Unidades.Quilo or Unidades.Litro;
        if (!fracionado && valor % 1 != 0 && unidade is not null) erros.Add($"{campo}: {unidade} precisa de número inteiro.");
        else if (decimal.Round(valor.Value, 3) != valor) erros.Add($"{campo} com mais de 3 casas decimais.");
        return valor;
    }

    private static bool? LerSimNao(Celula celula, List<string> erros, string campo)
    {
        if (celula.EstaVazia) return null;
        if (celula.Numero is double n) return n != 0;
        switch (Normalizar(celula.Texto!))
        {
            case "sim" or "s" or "x" or "1" or "true" or "verdadeiro": return true;
            case "nao" or "n" or "0" or "false" or "falso": return false;
            default: erros.Add($"{campo}: \"{celula.Texto}\" deve ser Sim ou Não."); return null;
        }
    }

    private static readonly string[] FormatosDeData = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"];

    private static DateOnly? LerData(Celula celula, List<string> erros)
    {
        if (celula.EstaVazia) return null;
        if (celula.Data is DateTime d) return DateOnly.FromDateTime(d);
        if (celula.Numero is double n && n is > 30000 and < 80000) return DateOnly.FromDateTime(DateTime.FromOADate(n)); // data "crua" do Excel
        if (celula.Texto is string t && DateOnly.TryParseExact(t.Trim(), FormatosDeData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            return data;
        erros.Add($"Validade: \"{celula.Texto}\" não é uma data (use dd/mm/aaaa).");
        return null;
    }
}
