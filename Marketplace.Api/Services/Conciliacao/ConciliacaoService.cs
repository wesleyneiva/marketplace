using System.Security.Cryptography;
using System.Text;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Conciliacao;

// Conciliação bancária: importa o extrato OFX e confere cada linha com o livro (MovimentosFinanceiros).
public class ConciliacaoService(AppDbContext db)
{
    private const decimal Tolerancia = 0.005m;   // centavo: valores iguais
    private const int DiasDeFolga = 3;           // o banco pode lançar 1–3 dias depois (fim de semana, D+1 útil)

    // ================================================================ IMPORTAR

    public async Task<ImportacaoExtratoResponse> ImportarAsync(byte[] arquivo, string nomeArquivo, int? contaId, string? usuarioId)
    {
        var ofx = LeitorOfx.Ler(arquivo);
        var conta = await EscolherContaAsync(ofx, contaId);
        string? aviso = null;

        // Conta já com número cadastrado e diferente do arquivo: avisa (pode ter escolhido a conta errada).
        if (!string.IsNullOrWhiteSpace(conta.Numero) && !string.IsNullOrWhiteSpace(ofx.Conta) && !MesmaConta(conta.Numero, ofx.Conta))
            aviso = $"O arquivo é da conta {ofx.Conta}, mas \"{conta.Nome}\" está cadastrada como {conta.Numero}. Confira se escolheu a conta certa.";
        // Primeira vez: guarda banco, agência e número (a próxima importação acha a conta sozinha).
        if (string.IsNullOrWhiteSpace(conta.Numero) && !string.IsNullOrWhiteSpace(ofx.Conta)) conta.Numero = Cortar(ofx.Conta, 40);
        if (string.IsNullOrWhiteSpace(conta.Agencia) && !string.IsNullOrWhiteSpace(ofx.Agencia)) conta.Agencia = Cortar(ofx.Agencia, 20);
        if (string.IsNullOrWhiteSpace(conta.Banco) && LeitorOfx.NomeBanco(ofx.Banco) is { } nb) conta.Banco = nb;

        // Não importar duas vezes: pela chave (FITID do banco) e, como garantia, pela contagem de linhas iguais
        // (alguns bancos trocam o FITID a cada download): se o banco já tinha 2 tarifas de R$ 5 no dia e o arquivo traz 2, não entra nenhuma.
        var chaves = ChavesDoArquivo(ofx.Transacoes);
        var existentes = await db.LinhasExtrato.AsNoTracking()
            .Where(l => l.ContaFinanceiraId == conta.Id && l.Data >= ofx.De && l.Data <= ofx.Ate)
            .Select(l => new { l.Chave, l.Data, l.Valor, l.Descricao }).ToListAsync();
        var chavesExistentes = existentes.Select(e => e.Chave).ToHashSet();
        var iguaisExistentes = existentes.GroupBy(e => (e.Data, e.Valor, Normal(e.Descricao))).ToDictionary(g => g.Key, g => g.Count());

        var extrato = new ExtratoBancario
        {
            ContaFinanceiraId = conta.Id, NomeArquivo = Cortar(nomeArquivo, 200), Banco = ofx.Banco is { } b ? Cortar(b, 10) : null,
            Agencia = ofx.Agencia is { } a ? Cortar(a, 20) : null, NumeroConta = ofx.Conta is { } c ? Cortar(c, 40) : null,
            De = ofx.De!.Value, Ate = ofx.Ate!.Value, SaldoFinal = ofx.Saldo, SaldoEm = ofx.Saldo is null ? null : ofx.SaldoEm ?? ofx.Ate,
            Linhas = ofx.Transacoes.Count, ImportadoPorId = usuarioId,
        };
        db.ExtratosBancarios.Add(extrato);

        var vistosNoArquivo = new Dictionary<(DateOnly, decimal, string), int>();
        var novas = 0;
        for (var i = 0; i < ofx.Transacoes.Count; i++)
        {
            var t = ofx.Transacoes[i];
            if (chavesExistentes.Contains(chaves[i])) continue;
            var igual = (t.Data, t.Valor, Normal(t.Descricao));
            vistosNoArquivo[igual] = vistosNoArquivo.GetValueOrDefault(igual) + 1;
            if (vistosNoArquivo[igual] <= iguaisExistentes.GetValueOrDefault(igual)) continue; // já veio com outro FITID
            db.LinhasExtrato.Add(new LinhaExtrato
            {
                ExtratoBancario = extrato, ContaFinanceiraId = conta.Id, Data = t.Data, Valor = t.Valor,
                Descricao = t.Descricao, Documento = t.Documento, TipoBanco = t.Tipo is { } tp ? Cortar(tp, 20) : null, Chave = chaves[i],
            });
            novas++;
        }
        extrato.Novas = novas;
        await db.SaveChangesAsync();

        var sugestoes = (await SugerirAsync(conta.Id)).Count;
        return new ImportacaoExtratoResponse(extrato.Id, conta.Id, conta.Nome, conta.Banco ?? LeitorOfx.NomeBanco(ofx.Banco),
            extrato.De, extrato.Ate, ofx.Transacoes.Count, novas, ofx.Transacoes.Count - novas, sugestoes, aviso);
    }

    private async Task<ContaFinanceira> EscolherContaAsync(LeitorOfx.Extrato ofx, int? contaId)
    {
        if (contaId is int id)
            return await db.ContasFinanceiras.FirstOrDefaultAsync(c => c.Id == id && c.Ativa && c.Tipo == TipoContaFinanceira.Banco)
                   ?? throw new ExtratoInvalidoException("Escolha uma conta de banco ativa.");
        var bancos = await db.ContasFinanceiras.Where(c => c.Ativa && c.Tipo == TipoContaFinanceira.Banco).ToListAsync();
        var pelaConta = string.IsNullOrWhiteSpace(ofx.Conta) ? [] : bancos.Where(c => c.Numero is { } n && MesmaConta(n, ofx.Conta)).ToList();
        if (pelaConta.Count == 1) return pelaConta[0];
        if (bancos.Count == 1) return bancos[0];
        throw new EscolherContaException(bancos.Count == 0
            ? "Cadastre a conta do banco em Financeiro → Saldos antes de importar o extrato."
            : $"Escolha em qual conta entra este extrato{(string.IsNullOrWhiteSpace(ofx.Conta) ? "" : $" (conta {ofx.Conta} no arquivo)")}.");
    }

    // "12345-6" e "0012345 6" são a mesma conta (compara só os dígitos, sem zeros à esquerda; o final basta).
    private static bool MesmaConta(string a, string b)
    {
        string D(string s) => new string(s.Where(char.IsDigit).ToArray()).TrimStart('0');
        var (x, y) = (D(a), D(b));
        if (x.Length == 0 || y.Length == 0) return false;
        return x == y || (Math.Min(x.Length, y.Length) >= 4 && (x.EndsWith(y) || y.EndsWith(x)));
    }

    // FITID do banco; repetido no mesmo arquivo (alguns bancos repetem) ou ausente → data + valor + descrição + ordem.
    private static List<string> ChavesDoArquivo(List<LeitorOfx.Transacao> ts)
    {
        var fitRepetido = ts.Where(t => t.FitId is not null).GroupBy(t => t.FitId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var ordem = new Dictionary<string, int>();
        return ts.Select(t =>
        {
            if (t.FitId is { } f && !fitRepetido.Contains(f)) return Cortar("F:" + f, 120);
            var basico = $"{t.Data:yyyyMMdd}|{t.Valor:0.00}|{Normal(t.Descricao)}|{t.FitId}";
            ordem[basico] = ordem.GetValueOrDefault(basico) + 1;
            return "H:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(basico + "|" + ordem[basico])));
        }).ToList();
    }

    // ================================================================ SUGERIR

    // Um "grupo" do livro: um lançamento sozinho ou o depósito da maquininha (entrada do bruto + saída da taxa = o líquido).
    private sealed record GrupoLivro(List<MovimentoFinanceiro> Movs, DateOnly Data, decimal Valor)
    {
        public string Descricao => Movs.OrderByDescending(m => m.Valor).First().Descricao;
    }

    private sealed record Sugestao(string Tipo, List<LinhaExtrato> Linhas, List<GrupoLivro> Grupos, string Explicacao)
    {
        public IEnumerable<MovimentoFinanceiro> Movs => Grupos.SelectMany(g => g.Movs);
    }

    private async Task<List<GrupoLivro>> GruposLivresAsync(int contaId, DateOnly de, DateOnly ate)
    {
        var movs = await db.MovimentosFinanceiros.AsNoTracking().Include(m => m.CategoriaFinanceira)
            .Where(m => m.ContaFinanceiraId == contaId && m.ConciliacaoId == null && m.Data >= de && m.Data <= ate)
            .OrderBy(m => m.Data).ThenBy(m => m.Id).ToListAsync();
        static decimal Sinal(MovimentoFinanceiro m) => m.Tipo == TipoMovimentoFinanceiro.Entrada ? m.Valor : -m.Valor;
        return movs.GroupBy(m => m.LiquidacaoId is { } g ? "L" + g : "M" + m.Id)
            .Select(g => new GrupoLivro(g.ToList(), g.Min(m => m.Data), g.Sum(Sinal))).Where(g => g.Valor != 0).ToList();
    }

    private async Task<List<Sugestao>> SugerirAsync(int contaId)
    {
        var linhas = await db.LinhasExtrato.AsNoTracking()
            .Where(l => l.ContaFinanceiraId == contaId && l.Status == StatusLinhaExtrato.Pendente)
            .OrderBy(l => l.Data).ThenBy(l => l.Id).ToListAsync();
        if (linhas.Count == 0) return [];
        var grupos = await GruposLivresAsync(contaId, linhas.Min(l => l.Data).AddDays(-DiasDeFolga), linhas.Max(l => l.Data).AddDays(DiasDeFolga));
        var sugestoes = new List<Sugestao>();
        var linhaUsada = new HashSet<int>();
        var grupoUsado = new HashSet<GrupoLivro>();

        // 1) Mesmo valor, data próxima (1 linha = 1 grupo). Ganha o par mais parecido: data mais perto, mesmo documento, descrição parecida.
        var pares = (from l in linhas
                     from g in grupos
                     where Math.Abs(l.Valor - g.Valor) < Tolerancia
                     let dias = Math.Abs(l.Data.DayNumber - g.Data.DayNumber)
                     where dias <= DiasDeFolga
                     let doc = l.Documento is { Length: > 2 } d && g.Movs.Any(m => m.Documento is { } md && md.Contains(d)) ? 30 : 0
                     let texto = Parecido(l.Descricao, g.Descricao) ? 5 : 0
                     orderby dias * 10 - doc - texto, l.Id
                     select (l, g, dias)).ToList();
        foreach (var (l, g, dias) in pares)
        {
            if (linhaUsada.Contains(l.Id) || grupoUsado.Contains(g)) continue;
            linhaUsada.Add(l.Id); grupoUsado.Add(g);
            var explica = g.Movs.Count > 1 ? "depósito da maquininha: valor bruto menos a taxa" : dias == 0 ? "mesmo valor e mesma data" : $"mesmo valor, {dias} dia(s) de diferença";
            sugestoes.Add(new Sugestao("exata", [l], [g], explica));
        }

        // 2) Várias linhas do banco = um lançamento do sistema (o banco mostra cada PIX; o sistema guarda o total do dia).
        foreach (var g in grupos.Where(g => !grupoUsado.Contains(g)).OrderByDescending(g => Math.Abs(g.Valor)))
        {
            var mesmoSinal = linhas.Where(l => !linhaUsada.Contains(l.Id) && Math.Sign(l.Valor) == Math.Sign(g.Valor) && l.Data == g.Data).ToList();
            if (mesmoSinal.Count < 2) continue;
            var pix = mesmoSinal.Where(l => Normal(l.Descricao).Contains("pix")).ToList();
            foreach (var conjunto in new[] { pix, mesmoSinal })
            {
                if (conjunto.Count < 2 || Math.Abs(conjunto.Sum(l => l.Valor) - g.Valor) >= Tolerancia) continue;
                foreach (var l in conjunto) linhaUsada.Add(l.Id);
                grupoUsado.Add(g);
                sugestoes.Add(new Sugestao("agrupada", conjunto, [g], $"{conjunto.Count} lançamentos do banco somam o valor do dia no sistema"));
                break;
            }
        }

        // 3) Os PIX do dia: o banco mostra cada um; o sistema guarda o PIX de cada caixa no fechamento.
        //    Soma de todos os PIX do banco no dia = soma dos PIX de todos os caixas no dia.
        foreach (var dia in linhas.Where(l => !linhaUsada.Contains(l.Id) && l.Valor > 0 && Normal(l.Descricao).Contains("pix")).GroupBy(l => l.Data))
        {
            var doDia = grupos.Where(g => !grupoUsado.Contains(g) && g.Valor > 0 && g.Data == dia.Key
                                          && g.Movs.All(m => m.Origem == OrigemMovimentoFinanceiro.Caixa && Normal(m.Descricao).Contains("pix"))).ToList();
            if (doDia.Count == 0 || dia.Count() + doDia.Count < 3) continue;
            if (Math.Abs(dia.Sum(l => l.Valor) - doDia.Sum(g => g.Valor)) >= Tolerancia) continue;
            foreach (var l in dia) linhaUsada.Add(l.Id);
            foreach (var g in doDia) grupoUsado.Add(g);
            sugestoes.Add(new Sugestao("agrupada", dia.ToList(), doDia,
                $"{dia.Count()} PIX do banco somam os PIX {(doDia.Count == 1 ? "do caixa" : $"dos {doDia.Count} caixas")} no dia"));
        }
        return sugestoes;
    }

    // ================================================================ CONSULTAR

    public async Task<ConciliacaoResponse> ConsultarAsync(int? contaId)
    {
        var bancos = await db.ContasFinanceiras.AsNoTracking().Where(c => c.Tipo == TipoContaFinanceira.Banco && c.Ativa).OrderBy(c => c.Nome).ToListAsync();
        var pendentesPorConta = await db.LinhasExtrato.Where(l => l.Status == StatusLinhaExtrato.Pendente)
            .GroupBy(l => l.ContaFinanceiraId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var ultimos = await db.ExtratosBancarios.GroupBy(e => e.ContaFinanceiraId).Select(g => new { g.Key, Ate = g.Max(e => e.Ate) })
            .ToDictionaryAsync(x => x.Key, x => x.Ate);
        var contas = bancos.Select(c => new ContaConciliacaoResponse(c.Id, c.Nome, c.Banco, pendentesPorConta.GetValueOrDefault(c.Id),
            ultimos.TryGetValue(c.Id, out var u) ? u : null)).ToList();

        // Sem conta escolhida: a que tem extrato mais recente (ou a primeira).
        var id = contaId ?? contas.OrderByDescending(c => c.UltimoExtratoAte).FirstOrDefault()?.Id;
        if (id is not int cid || bancos.All(b => b.Id != cid))
            return new ConciliacaoResponse(contas, null, null, null, null, null, null, 0, 0, [], [], [], []);

        var categorias = await db.CategoriasFinanceiras.AsNoTracking().ToListAsync();
        var sugestoes = await SugerirAsync(cid);
        var sugestaoDaLinha = sugestoes.SelectMany(s => s.Linhas.Select(l => (l.Id, s))).ToDictionary(x => x.Id, x => x.s);

        var pendentes = await db.LinhasExtrato.AsNoTracking().Where(l => l.ContaFinanceiraId == cid && l.Status == StatusLinhaExtrato.Pendente)
            .OrderBy(l => l.Data).ThenBy(l => l.Id).ToListAsync();
        var linhasResp = pendentes.Select(l => Linha(l, sugestaoDaLinha.GetValueOrDefault(l.Id),
            sugestaoDaLinha.ContainsKey(l.Id) ? null : Financeiro.SugerirCategoria(l.Descricao, categorias,
                l.Valor > 0 ? NaturezaFinanceira.Entrada : NaturezaFinanceira.Saida))).ToList();

        // Período coberto pelos extratos e o último saldo informado pelo banco.
        var extratos = await db.ExtratosBancarios.AsNoTracking().Where(e => e.ContaFinanceiraId == cid).ToListAsync();
        DateOnly? de = extratos.Count > 0 ? extratos.Min(e => e.De) : null, ate = extratos.Count > 0 ? extratos.Max(e => e.Ate) : null;
        var comSaldo = extratos.Where(e => e.SaldoFinal is not null).OrderByDescending(e => e.SaldoEm).ThenByDescending(e => e.Id).FirstOrDefault();
        decimal? saldoSistema = comSaldo is null ? null : (await Financeiro.SaldosAsync(db, comSaldo.SaldoEm)).GetValueOrDefault(cid);

        // No sistema e não no banco (dentro do período dos extratos), tirando o que já está numa sugestão.
        var semExtrato = new List<MovimentoResumo>();
        if (de is { } d0 && ate is { } d1)
        {
            var sugeridos = sugestoes.SelectMany(s => s.Movs.Select(m => m.Id)).ToHashSet();
            semExtrato = (await GruposLivresAsync(cid, d0, d1)).SelectMany(g => g.Movs).Where(m => !sugeridos.Contains(m.Id))
                .Take(300).Select(Resumo).ToList();
        }

        var conciliadas = await db.LinhasExtrato.CountAsync(l => l.ContaFinanceiraId == cid && l.Status == StatusLinhaExtrato.Conciliada);
        var ignoradas = await db.LinhasExtrato.AsNoTracking().Where(l => l.ContaFinanceiraId == cid && l.Status == StatusLinhaExtrato.Ignorada)
            .OrderByDescending(l => l.Data).Take(100).ToListAsync();
        var totalIgnoradas = await db.LinhasExtrato.CountAsync(l => l.ContaFinanceiraId == cid && l.Status == StatusLinhaExtrato.Ignorada);

        // Últimas conferências (para desfazer, se preciso).
        var ultimasIds = await db.LinhasExtrato.Where(l => l.ContaFinanceiraId == cid && l.Status == StatusLinhaExtrato.Conciliada && l.ConciliacaoId != null)
            .GroupBy(l => l.ConciliacaoId).Select(g => new { Id = g.Key!.Value, Em = g.Max(l => l.ConciliadaEm), Data = g.Max(l => l.Data) })
            .OrderByDescending(x => x.Em).ThenByDescending(x => x.Data).Take(40).ToListAsync();
        var gids = ultimasIds.Select(x => (Guid?)x.Id).ToList();
        var linhasG = await db.LinhasExtrato.AsNoTracking().Where(l => gids.Contains(l.ConciliacaoId)).ToListAsync();
        var movsG = await db.MovimentosFinanceiros.AsNoTracking().Include(m => m.CategoriaFinanceira).Where(m => gids.Contains(m.ConciliacaoId)).ToListAsync();
        var recentes = ultimasIds.Select(x => new ConciliacaoGrupoResponse(x.Id, x.Em,
            linhasG.Where(l => l.ConciliacaoId == x.Id).OrderBy(l => l.Data).Select(l => Linha(l, null, null)).ToList(),
            movsG.Where(m => m.ConciliacaoId == x.Id).Select(Resumo).ToList())).ToList();

        return new ConciliacaoResponse(contas, cid, comSaldo?.SaldoFinal, comSaldo?.SaldoEm, saldoSistema, de, ate,
            conciliadas, totalIgnoradas, linhasResp, semExtrato, recentes, ignoradas.Select(l => Linha(l, null, null)).ToList());
    }

    // Lançamentos livres que podem ser essa linha (para o cliente escolher na mão): mesmo sinal, até 15 dias de distância.
    public async Task<List<MovimentoResumo>> CandidatosAsync(int linhaId)
    {
        var l = await db.LinhasExtrato.AsNoTracking().FirstOrDefaultAsync(x => x.Id == linhaId) ?? throw new ConciliacaoException("Linha do extrato não encontrada.");
        var tipo = l.Valor > 0 ? TipoMovimentoFinanceiro.Entrada : TipoMovimentoFinanceiro.Saida;
        var movs = await db.MovimentosFinanceiros.AsNoTracking().Include(m => m.CategoriaFinanceira)
            .Where(m => m.ContaFinanceiraId == l.ContaFinanceiraId && m.ConciliacaoId == null && m.Tipo == tipo
                        && m.Data >= l.Data.AddDays(-15) && m.Data <= l.Data.AddDays(15)).ToListAsync();
        var valor = Math.Abs(l.Valor);
        return movs.OrderBy(m => Math.Abs(m.Valor - valor) < Tolerancia ? 0 : 1).ThenBy(m => Math.Abs(m.Data.DayNumber - l.Data.DayNumber))
            .ThenBy(m => Math.Abs(m.Valor - valor)).Take(40).Select(Resumo).ToList();
    }

    // ================================================================ AÇÕES

    // Confere estas linhas com estes lançamentos: as somas têm de bater (no centavo).
    public async Task<Guid> ConciliarAsync(List<int> linhaIds, List<int> movimentoIds, string? usuarioId)
    {
        if (linhaIds.Count == 0 || movimentoIds.Count == 0) throw new ConciliacaoException("Escolha a linha do extrato e o lançamento do sistema.");
        var linhas = await db.LinhasExtrato.Where(l => linhaIds.Contains(l.Id)).ToListAsync();
        var movs = await db.MovimentosFinanceiros.Where(m => movimentoIds.Contains(m.Id)).ToListAsync();
        if (linhas.Count != linhaIds.Distinct().Count() || movs.Count != movimentoIds.Distinct().Count())
            throw new ConciliacaoException("Algum item não foi encontrado: atualize a tela.");
        if (linhas.Any(l => l.Status != StatusLinhaExtrato.Pendente)) throw new ConciliacaoException("Esta linha do extrato já foi conferida ou ignorada.");
        if (movs.Any(m => m.ConciliacaoId != null)) throw new ConciliacaoException("Este lançamento já foi conferido com outra linha do extrato.");
        var conta = linhas[0].ContaFinanceiraId;
        if (linhas.Any(l => l.ContaFinanceiraId != conta) || movs.Any(m => m.ContaFinanceiraId != conta))
            throw new ConciliacaoException("O extrato e o lançamento precisam ser da mesma conta.");
        var banco = linhas.Sum(l => l.Valor);
        var sistema = movs.Sum(m => m.Tipo == TipoMovimentoFinanceiro.Entrada ? m.Valor : -m.Valor);
        if (Math.Abs(banco - sistema) >= Tolerancia)
            throw new ConciliacaoException($"Os valores não batem: no banco {Moeda(banco)}, no sistema {Moeda(sistema)} (diferença de {Moeda(banco - sistema)}).");

        var id = Guid.NewGuid();
        Marcar(linhas, movs, id, usuarioId);
        await db.SaveChangesAsync();
        return id;
    }

    // Aceita todas as sugestões do sistema de uma vez.
    public async Task<int> AceitarSugestoesAsync(int contaId, string? usuarioId)
    {
        var sugestoes = await SugerirAsync(contaId);
        if (sugestoes.Count == 0) return 0;
        var linhaIds = sugestoes.SelectMany(s => s.Linhas.Select(l => l.Id)).ToList();
        var movIds = sugestoes.SelectMany(s => s.Movs.Select(m => m.Id)).ToList();
        var linhas = await db.LinhasExtrato.Where(l => linhaIds.Contains(l.Id) && l.Status == StatusLinhaExtrato.Pendente).ToDictionaryAsync(l => l.Id);
        var movs = await db.MovimentosFinanceiros.Where(m => movIds.Contains(m.Id) && m.ConciliacaoId == null).ToDictionaryAsync(m => m.Id);
        var feitas = 0;
        foreach (var s in sugestoes)
        {
            if (s.Linhas.Any(l => !linhas.ContainsKey(l.Id)) || s.Movs.Any(m => !movs.ContainsKey(m.Id))) continue;
            Marcar(s.Linhas.Select(l => linhas[l.Id]).ToList(), s.Movs.Select(m => movs[m.Id]).ToList(), Guid.NewGuid(), usuarioId);
            feitas++;
        }
        await db.SaveChangesAsync();
        return feitas;
    }

    // A linha do banco não está no sistema (tarifa, PIX que ninguém lançou): lança agora, já conferida.
    public async Task<MovimentoResumo> LancarAsync(int linhaId, int categoriaId, string? descricao, string? usuarioId)
    {
        var l = await db.LinhasExtrato.FirstOrDefaultAsync(x => x.Id == linhaId) ?? throw new ConciliacaoException("Linha do extrato não encontrada.");
        if (l.Status != StatusLinhaExtrato.Pendente) throw new ConciliacaoException("Esta linha do extrato já foi conferida ou ignorada.");
        var natureza = l.Valor > 0 ? NaturezaFinanceira.Entrada : NaturezaFinanceira.Saida;
        var cat = await db.CategoriasFinanceiras.FirstOrDefaultAsync(c => c.Id == categoriaId && c.Ativa)
                  ?? throw new ConciliacaoException("Escolha a categoria.");
        if (cat.Natureza != natureza)
            throw new ConciliacaoException(natureza == NaturezaFinanceira.Entrada ? "Dinheiro que entrou precisa de uma categoria de receita." : "Dinheiro que saiu precisa de uma categoria de despesa.");
        if (cat.Grupo == GrupoFinanceiro.ReceitaVendas)
            throw new ConciliacaoException("As vendas do caixa entram sozinhas pelo fechamento do caixa: escolha outra categoria.");

        var m = new MovimentoFinanceiro
        {
            Data = l.Data, Tipo = natureza == NaturezaFinanceira.Entrada ? TipoMovimentoFinanceiro.Entrada : TipoMovimentoFinanceiro.Saida,
            Valor = Math.Abs(l.Valor), ContaFinanceiraId = l.ContaFinanceiraId, CategoriaFinanceiraId = cat.Id,
            Descricao = Cortar(string.IsNullOrWhiteSpace(descricao) ? l.Descricao : descricao.Trim(), 200), Documento = l.Documento,
            Origem = OrigemMovimentoFinanceiro.Manual, CriadoPorId = usuarioId,
        };
        db.MovimentosFinanceiros.Add(m);
        Marcar([l], [m], Guid.NewGuid(), usuarioId);
        await db.SaveChangesAsync();
        m.CategoriaFinanceira = cat;
        return Resumo(m);
    }

    public async Task IgnorarAsync(int linhaId, bool ignorar)
    {
        var l = await db.LinhasExtrato.FirstOrDefaultAsync(x => x.Id == linhaId) ?? throw new ConciliacaoException("Linha do extrato não encontrada.");
        if (ignorar && l.Status != StatusLinhaExtrato.Pendente) throw new ConciliacaoException("Só dá para ignorar uma linha ainda não conferida.");
        if (!ignorar && l.Status != StatusLinhaExtrato.Ignorada) throw new ConciliacaoException("Esta linha não está ignorada.");
        l.Status = ignorar ? StatusLinhaExtrato.Ignorada : StatusLinhaExtrato.Pendente;
        await db.SaveChangesAsync();
    }

    // Desfaz uma conferência: as linhas voltam a pendentes e os lançamentos ficam livres (nada é apagado).
    public async Task DesfazerAsync(Guid conciliacaoId)
    {
        var linhas = await db.LinhasExtrato.Where(l => l.ConciliacaoId == conciliacaoId).ToListAsync();
        if (linhas.Count == 0) throw new ConciliacaoException("Conferência não encontrada.");
        foreach (var l in linhas) { l.Status = StatusLinhaExtrato.Pendente; l.ConciliacaoId = null; l.ConciliadaEm = null; l.ConciliadaPorId = null; }
        await db.MovimentosFinanceiros.Where(m => m.ConciliacaoId == conciliacaoId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ConciliacaoId, (Guid?)null).SetProperty(m => m.Conciliado, false));
        await db.SaveChangesAsync();
    }

    // ================================================================ detalhes

    private static void Marcar(List<LinhaExtrato> linhas, List<MovimentoFinanceiro> movs, Guid id, string? usuarioId)
    {
        var agora = DateTimeOffset.UtcNow;
        foreach (var l in linhas) { l.Status = StatusLinhaExtrato.Conciliada; l.ConciliacaoId = id; l.ConciliadaEm = agora; l.ConciliadaPorId = usuarioId; }
        foreach (var m in movs) { m.ConciliacaoId = id; m.Conciliado = true; }
    }

    private static LinhaExtratoResponse Linha(LinhaExtrato l, Sugestao? s, int? categoria) =>
        new(l.Id, l.Data, l.Valor, l.Descricao, l.Documento, l.Status, l.ConciliacaoId,
            s is null ? null : new SugestaoConciliacaoResponse(s.Tipo, s.Linhas.Select(x => x.Id).ToList(), s.Movs.Select(m => m.Id).ToList(),
                s.Movs.Select(Resumo).ToList(), s.Explicacao),
            categoria);

    private static MovimentoResumo Resumo(MovimentoFinanceiro m) =>
        new(m.Id, m.Data, m.Tipo, m.Valor, m.Descricao, m.CategoriaFinanceira?.Nome, m.Origem);

    // Descrições parecidas: uma palavra de 4+ letras em comum ("CEEE" na conta de luz e no débito automático).
    private static bool Parecido(string a, string b)
    {
        var pa = Normal(a).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 4 && !p.All(char.IsDigit)).ToHashSet();
        return Normal(b).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(pa.Contains);
    }

    private static string Normal(string s) =>
        System.Text.RegularExpressions.Regex.Replace(ImportacaoProdutosService.Normalizar(s), "[^a-z0-9]+", " ").Trim();

    private static string Cortar(string v, int max) => v.Length <= max ? v : v[..max];
    private static string Moeda(decimal v) => v.ToString("C", new System.Globalization.CultureInfo("pt-BR"));
}

public class ConciliacaoException(string mensagem) : Exception(mensagem);
public class EscolherContaException(string mensagem) : Exception(mensagem);
