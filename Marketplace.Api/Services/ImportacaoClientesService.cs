using System.Globalization;
using ClosedXML.Excel;
using Marketplace.Api.Contracts;
using Marketplace.Api.Controllers;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;
using static Marketplace.Api.Services.LeitorPlanilha;

namespace Marketplace.Api.Services;

// Importação de clientes por planilha (a loja que já funciona traz a lista do sistema antigo), no mesmo jeito da de
// produtos: 1) modelo .xlsx, 2) prévia linha por linha (nada é gravado), 3) grava as linhas sem erro.
// Quem é quem: pelo CPF e, sem CPF, pelo telefone. Cliente que já existe é ATUALIZADO (só com o que veio preenchido),
// então importar de novo não duplica. Só o nome e "telefone ou CPF" travam a linha; o resto errado vira aviso e fica
// de fora (um e-mail torto não pode impedir o cliente de entrar).
public class ImportacaoClientesService(AppDbContext db)
{
    public const int MaxLinhas = 20000;
    public const string AbaClientes = "Clientes";

    public record Coluna(string Titulo, bool Obrigatoria, string Explicacao, string Exemplo, bool SoOficina = false);

    // A ORDEM aqui é a ordem das colunas no Excel. As da oficina (raquete, corda, tensão) só entram no modelo do ramo com serviços.
    public static readonly Coluna[] Colunas =
    [
        new("Nome", true, "Nome do cliente (2 a 120 letras).", "Maria da Silva"),
        new("Apelido", false, "Como o caixa procura o cliente (ex.: \"Dona Maria do 302\"). Opcional.", "Dona Maria do 302"),
        new("Telefone", false, "WhatsApp com DDD (ex.: 51 99999-8888). Telefone OU CPF é obrigatório: é por eles que o sistema " +
            "reconhece o cliente e não duplica.", "(51) 99999-8888"),
        new("CPF", false, "Só os números ou com pontos (ex.: 123.456.789-09). Se o cliente já existir com este CPF, a importação " +
            "ATUALIZA o cadastro em vez de criar outro.", "123.456.789-09"),
        new("E-mail", false, "Opcional.", "maria@email.com"),
        new("Endereço", false, "Opcional (até 200 letras).", "Rua das Flores, 120 - Centro"),
        new("Nascimento", false, "Data de nascimento (dd/mm/aaaa). Opcional.", "15/03/1980"),
        new("Observação", false, "Qualquer anotação (até 300 letras).", "Prefere receber ofertas por WhatsApp"),
        new("Raquete", false, "Raquete que o cliente usa (para a oficina).", "Babolat Pure Aero 2023", SoOficina: true),
        new("Corda", false, "Corda que o cliente usa.", "RPM Blast 1.25", SoOficina: true),
        new("Tensão", false, "Tensão do encordoamento (ex.: 24/22 lb).", "24/22 lb", SoOficina: true),
    ];

    private const int Nome = 0, Apelido = 1, Telefone = 2, Cpf = 3, Email = 4, Endereco = 5, Nascimento = 6, Observacao = 7,
        Raquete = 8, Corda = 9, Tensao = 10;

    // Outros nomes aceitos no cabeçalho (lista exportada de outro sistema). Comparados sem acento e sem maiúsculas.
    private static readonly Dictionary<string, int> NomesDasColunas = new()
    {
        ["nome"] = Nome, ["cliente"] = Nome, ["nome do cliente"] = Nome, ["nome completo"] = Nome, ["razao social"] = Nome,
        ["apelido"] = Apelido, ["nome fantasia"] = Apelido, ["conhecido como"] = Apelido,
        ["telefone"] = Telefone, ["celular"] = Telefone, ["whatsapp"] = Telefone, ["fone"] = Telefone, ["tel"] = Telefone,
        ["telefone celular"] = Telefone, ["contato"] = Telefone, ["telefone 1"] = Telefone,
        ["cpf"] = Cpf, ["cpf/cnpj"] = Cpf, ["cpf cnpj"] = Cpf, ["documento"] = Cpf,
        ["e-mail"] = Email, ["email"] = Email,
        ["endereco"] = Endereco, ["logradouro"] = Endereco, ["endereco completo"] = Endereco,
        ["nascimento"] = Nascimento, ["data de nascimento"] = Nascimento, ["aniversario"] = Nascimento, ["data nasc"] = Nascimento,
        ["dt nascimento"] = Nascimento,
        ["observacao"] = Observacao, ["observacoes"] = Observacao, ["obs"] = Observacao, ["anotacoes"] = Observacao,
        ["raquete"] = Raquete, ["corda"] = Corda, ["tensao"] = Tensao,
    };

    // ================================================================ modelo

    public byte[] GerarModelo(bool comOficina)
    {
        var colunas = Colunas.Where(c => comOficina || !c.SoOficina).ToArray();
        using var planilha = new XLWorkbook();
        var clientes = planilha.AddWorksheet(AbaClientes);
        var exemplo = planilha.AddWorksheet("Exemplo");
        var instrucoes = planilha.AddWorksheet("Instruções");
        MontarAba(clientes, colunas);
        MontarAba(exemplo, colunas);
        object?[][] linhas =
        [
            ["Maria da Silva", "Dona Maria do 302", "(51) 99999-8888", "529.982.247-25", "maria@email.com", "Rua das Flores, 120", new DateTime(1980, 3, 15), null, "Babolat Pure Aero", "RPM Blast 1.25", "24/22 lb"],
            ["João Pereira", null, "51 98888-7777", null, null, null, null, "Cliente desde 2019", null, null, null],
            ["Ana Souza", "Ana da academia", null, "111.444.777-35", null, null, new DateTime(1995, 11, 2), null, null, null, null],
        ];
        for (var l = 0; l < linhas.Length; l++)
            for (var c = 0; c < colunas.Length; c++)
                exemplo.Cell(l + 2, c + 1).Value = linhas[l][c] switch
                {
                    null => Blank.Value,
                    string s => s,
                    DateTime d => d,
                    _ => throw new InvalidOperationException(),
                };
        exemplo.Range(2, 1, linhas.Length + 1, colunas.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#dcfce7");
        exemplo.Cell(linhas.Length + 3, 1).Value = "Esta aba é só um exemplo e NÃO é importada. Preencha os seus clientes na aba \"Clientes\".";
        exemplo.Cell(linhas.Length + 3, 1).Style.Font.Italic = true;

        string[] passos =
        [
            "Como importar os seus clientes",
            "",
            "1. Preencha a aba \"Clientes\": uma linha por cliente, a partir da linha 2. Não mude os títulos da linha 1.",
            "2. Nome é obrigatório, e também o telefone OU o CPF (é por eles que o sistema reconhece o cliente).",
            "3. Salve o arquivo (.xlsx) e envie na tela Clientes → Importar planilha.",
            "4. O sistema mostra uma PRÉVIA com o que acontece em cada linha antes de gravar qualquer coisa.",
            "5. Pode importar de novo quando quiser: cliente com o mesmo CPF (ou, sem CPF, o mesmo telefone) é ATUALIZADO, não duplicado.",
            "",
            "Dica: exporte a lista de clientes do seu sistema antigo para Excel e copie as colunas para a aba \"Clientes\".",
            "Também aceitamos .csv (separado por ponto e vírgula, como o Excel brasileiro salva).",
            "Histórico de compras e saldo devedor do sistema antigo não entram por aqui.",
        ];
        for (var i = 0; i < passos.Length; i++) instrucoes.Cell(i + 1, 1).Value = passos[i];
        instrucoes.Cell(1, 1).Style.Font.Bold = true;
        instrucoes.Cell(1, 1).Style.Font.FontSize = 14;
        var inicio = passos.Length + 2;
        string[] titulos = ["Coluna", "Obrigatória?", "O que colocar", "Exemplo"];
        for (var i = 0; i < titulos.Length; i++) instrucoes.Cell(inicio, i + 1).Value = titulos[i];
        instrucoes.Range(inicio, 1, inicio, 4).Style.Font.Bold = true;
        for (var i = 0; i < colunas.Length; i++)
        {
            instrucoes.Cell(inicio + 1 + i, 1).Value = colunas[i].Titulo;
            instrucoes.Cell(inicio + 1 + i, 2).Value = colunas[i].Obrigatoria ? "Sim" : "Não";
            instrucoes.Cell(inicio + 1 + i, 3).Value = colunas[i].Explicacao;
            instrucoes.Cell(inicio + 1 + i, 4).SetValue(colunas[i].Exemplo);
        }
        instrucoes.Column(1).Width = 16;
        instrucoes.Column(2).Width = 13;
        instrucoes.Column(3).Width = 90;
        instrucoes.Column(4).Width = 26;
        instrucoes.Range(inicio + 1, 3, inicio + colunas.Length, 3).Style.Alignment.WrapText = true;

        clientes.SetTabActive();
        using var memoria = new MemoryStream();
        planilha.SaveAs(memoria);
        return memoria.ToArray();
    }

    private static void MontarAba(IXLWorksheet aba, Coluna[] colunas)
    {
        for (var i = 0; i < colunas.Length; i++)
        {
            var celula = aba.Cell(1, i + 1);
            celula.Value = colunas[i].Titulo + (colunas[i].Obrigatoria ? " *" : "");
            celula.GetComment().AddText(colunas[i].Explicacao);
        }
        var cabecalho = aba.Range(1, 1, 1, colunas.Length);
        cabecalho.Style.Font.Bold = true;
        cabecalho.Style.Font.FontColor = XLColor.White;
        cabecalho.Style.Fill.BackgroundColor = XLColor.FromHtml("#15803d");
        aba.Row(1).Height = 22;
        aba.SheetView.FreezeRows(1);
        // Telefone e CPF como TEXTO (senão o Excel come o zero do começo do CPF e mostra 5,19E+10 no telefone).
        aba.Column(Telefone + 1).Style.NumberFormat.Format = "@";
        aba.Column(Cpf + 1).Style.NumberFormat.Format = "@";
        aba.Column(Nascimento + 1).Style.NumberFormat.Format = "dd/mm/yyyy";
        int[] larguras = [32, 22, 18, 17, 28, 34, 13, 34, 26, 18, 12];
        for (var i = 0; i < colunas.Length; i++) aba.Column(i + 1).Width = larguras[i];
    }

    // ================================================================ análise (prévia e gravação usam a mesma)

    internal class LinhaAnalisada
    {
        public int Linha { get; init; }
        public string? Nome, Apelido, Telefone, Cpf, Email, Endereco, Observacao, Raquete, Corda, Tensao;
        public DateOnly? Nascimento;
        public int? ExistenteId { get; set; }
        public List<string> Erros { get; } = [];
        public List<string> Avisos { get; } = [];
        public string Acao => Erros.Count > 0 ? "Erro" : ExistenteId is null ? "Novo" : "Atualizar";
    }

    internal record Analise(List<LinhaAnalisada> Linhas, List<string> ErrosGerais, List<string> AvisosGerais);

    public async Task<PreviaImportacaoClientesResponse> GerarPreviaAsync(Stream arquivo, string nomeDoArquivo)
    {
        var (linhas, errosGerais, avisosGerais) = await AnalisarAsync(arquivo, nomeDoArquivo);
        return new(nomeDoArquivo, linhas.Count, linhas.Count(l => l.Acao == "Novo"), linhas.Count(l => l.Acao == "Atualizar"),
            linhas.Count(l => l.Acao == "Erro"), linhas.Count(l => l.Avisos.Count > 0), errosGerais, avisosGerais,
            linhas.Select(l => new LinhaPreviaClienteResponse(l.Linha, l.Acao, l.Nome, l.Apelido, l.Telefone, l.Cpf, l.Email,
                l.Nascimento, l.Erros, l.Avisos)).ToList());
    }

    internal async Task<Analise> AnalisarAsync(Stream arquivo, string nomeDoArquivo)
    {
        Resultado planilha;
        try { planilha = Ler(arquivo, nomeDoArquivo, AbaClientes, MaxLinhas); }
        catch (PlanilhaInvalidaException e) { return new([], [e.Message], []); }

        var posicao = new int?[Colunas.Length];
        var avisosGerais = new List<string>();
        for (var c = 0; c < planilha.Cabecalho.Length; c++)
        {
            var titulo = planilha.Cabecalho[c];
            if (string.IsNullOrWhiteSpace(titulo)) continue;
            if (NomesDasColunas.TryGetValue(Normalizar(titulo.Replace("*", "")), out var campo) && posicao[campo] is null) posicao[campo] = c;
            else avisosGerais.Add($"A coluna \"{titulo}\" não foi reconhecida e será ignorada.");
        }
        if (planilha.Linhas.Count == 0)
            return new([], ["A planilha está vazia: preencha os clientes a partir da linha 2 da aba \"Clientes\"."], avisosGerais);
        if (posicao[Nome] is null)
            return new([], ["Falta a coluna \"Nome\" na linha 1. Use o modelo da planilha."], avisosGerais);
        if (posicao[Telefone] is null && posicao[Cpf] is null)
            return new([], ["Falta a coluna \"Telefone\" ou \"CPF\" na linha 1: é por eles que o sistema reconhece cada cliente."], avisosGerais);

        // O que já existe (só desta empresa: o filtro global cuida disso).
        var existentes = await db.Clientes.AsNoTracking().Select(c => new { c.Id, c.Nome, c.Cpf, c.Telefone, c.Ativo }).ToListAsync();
        var porCpf = existentes.Where(c => c.Cpf != null).GroupBy(c => c.Cpf!).ToDictionary(g => g.Key, g => g.First());
        var porTelefone = existentes.Where(c => c.Telefone != null).GroupBy(c => c.Telefone!).ToDictionary(g => g.Key, g => g.First());
        var cpfsVistos = new Dictionary<string, int>();
        var telefonesVistos = new Dictionary<string, int>();
        var resultado = new List<LinhaAnalisada>();

        foreach (var linha in planilha.Linhas)
        {
            Celula Valor(int campo) => posicao[campo] is int c && c < linha.Celulas.Length ? linha.Celulas[c] : Celula.Vazia;
            string? Texto(int campo) => Valor(campo) is { EstaVazia: false } v
                ? (v.Numero is double n ? n.ToString("0", CultureInfo.InvariantCulture) : v.Data is DateTime d ? d.ToString("dd/MM/yyyy") : v.Texto!.Trim())
                : null;
            var a = new LinhaAnalisada { Linha = linha.NumeroDaLinha };
            resultado.Add(a);

            a.Nome = Texto(Nome);
            if (string.IsNullOrWhiteSpace(a.Nome)) a.Erros.Add("Falta o nome.");
            else if (a.Nome.Length is < 2 or > 120) a.Erros.Add("O nome deve ter de 2 a 120 letras.");

            // CPF: número do Excel perde o zero do começo (01234567890 → 1234567890): devolve.
            var cpf = ClientesService.SoDigitos(Texto(Cpf));
            if (Valor(Cpf).Numero is not null && cpf.Length is 9 or 10) cpf = cpf.PadLeft(11, '0');
            if (cpf.Length == 14) { a.Avisos.Add("CNPJ não é guardado (o cadastro de cliente é por CPF): ficou de fora."); cpf = ""; }
            else if (cpf.Length > 0 && !FiadoController.CpfValido(cpf)) { a.Erros.Add($"CPF inválido: \"{Texto(Cpf)}\"."); cpf = ""; }
            a.Cpf = cpf.Length > 0 ? cpf : null;

            // Telefone: só dígitos, com DDD; "55" do país na frente sai (o cadastro guarda sem ele).
            var tel = ClientesService.SoDigitos(Texto(Telefone));
            if (tel.Length is 12 or 13 && tel.StartsWith("55")) tel = tel[2..];
            if (tel.Length is 8 or 9) { a.Avisos.Add("Telefone sem DDD: ficou de fora (coloque o DDD e importe de novo)."); tel = ""; }
            else if (tel.Length > 0 && tel.Length is < 10 or > 11) { a.Avisos.Add($"Telefone \"{Texto(Telefone)}\" não parece um número com DDD: ficou de fora."); tel = ""; }
            a.Telefone = tel.Length > 0 ? tel : null;

            if (a.Cpf is null && a.Telefone is null && !a.Erros.Any(e => e.StartsWith("CPF")))
                a.Erros.Add("Informe o telefone (com DDD) ou o CPF: é por eles que o sistema reconhece o cliente e não duplica.");

            // Repetidos dentro da planilha.
            if (a.Cpf is not null)
            {
                if (cpfsVistos.TryGetValue(a.Cpf, out var outra)) a.Erros.Add($"CPF repetido (já está na linha {outra}).");
                else cpfsVistos[a.Cpf] = a.Linha;
            }
            if (a.Telefone is not null)
            {
                if (telefonesVistos.TryGetValue(a.Telefone, out var outra)) a.Erros.Add($"Telefone repetido (já está na linha {outra}).");
                else telefonesVistos[a.Telefone] = a.Linha;
            }

            // Quem é: pelo CPF; sem CPF, pelo telefone. CPF de um e telefone de outro = não dá para saber.
            var peloCpf = a.Cpf is not null ? porCpf.GetValueOrDefault(a.Cpf) : null;
            var peloTelefone = a.Telefone is not null ? porTelefone.GetValueOrDefault(a.Telefone) : null;
            if (peloCpf is not null && peloTelefone is not null && peloCpf.Id != peloTelefone.Id)
                a.Erros.Add($"O CPF já é de \"{peloCpf.Nome}\" e o telefone já é de \"{peloTelefone.Nome}\": confira qual está certo.");
            else if (peloCpf is null && peloTelefone is { Cpf: not null } t && a.Cpf is not null)
                a.Erros.Add($"O telefone já é de \"{t.Nome}\", cadastrado com outro CPF.");
            else if (a.Erros.Count == 0 && (peloCpf ?? peloTelefone) is { } existente)
            {
                a.ExistenteId = existente.Id;
                if (a.Nome is not null && Normalizar(a.Nome) != Normalizar(existente.Nome)) a.Avisos.Add($"Já cadastrado como \"{existente.Nome}\": o nome muda.");
                if (!existente.Ativo) a.Avisos.Add("Estava desativado: volta a ficar ativo.");
            }

            a.Apelido = Cortar(Texto(Apelido), 60, "Apelido", a.Avisos);
            a.Endereco = Cortar(Texto(Endereco), 200, "Endereço", a.Avisos);
            a.Observacao = Cortar(Texto(Observacao), 300, "Observação", a.Avisos);
            a.Raquete = Cortar(Texto(Raquete), 120, "Raquete", a.Avisos);
            a.Corda = Cortar(Texto(Corda), 120, "Corda", a.Avisos);
            a.Tensao = ClientesService.Tensao(Texto(Tensao));

            var email = Texto(Email)?.ToLowerInvariant();
            if (email is not null && (!email.Contains('@') || !email.Contains('.') || email.Length > 120 || email.Contains(' ')))
            { a.Avisos.Add($"E-mail \"{email}\" inválido: ficou de fora."); email = null; }
            a.Email = email;

            a.Nascimento = LerNascimento(Valor(Nascimento), a.Avisos);
        }
        // Sem CPF entra (lista antiga quase nunca tem), mas o cadastro pede: um aviso só, não um por linha.
        var semCpf = resultado.Count(l => l.Erros.Count == 0 && l.Cpf is null);
        if (semCpf > 0)
            avisosGerais.Add($"{semCpf} {(semCpf == 1 ? "cliente entra" : "clientes entram")} sem CPF: o cadastro pede o CPF, complete quando o cliente vier (ou no próximo envio da planilha).");
        return new(resultado, [], avisosGerais);
    }

    private static string? Cortar(string? texto, int max, string campo, List<string> avisos)
    {
        if (texto is null || texto.Length <= max) return texto;
        avisos.Add($"{campo} com mais de {max} letras: foi cortado.");
        return texto[..max];
    }

    private static readonly string[] FormatosDeData = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"];

    private static DateOnly? LerNascimento(Celula c, List<string> avisos)
    {
        if (c.EstaVazia) return null;
        DateOnly? data = c.Data is DateTime d ? DateOnly.FromDateTime(d)
            : c.Numero is double n && n is > 365 and < 80000 ? DateOnly.FromDateTime(DateTime.FromOADate(n))
            : c.Texto is string t && DateOnly.TryParseExact(t.Trim(), FormatosDeData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) ? p
            : null;
        // "15/03/80" vira 2080: ano no futuro é do século passado.
        if (data is { } x && x > Relogio.Hoje && x.Year - 100 >= 1900) data = x.AddYears(-100);
        if (data is null || data > Relogio.Hoje || data.Value.Year < 1900)
        {
            avisos.Add($"Nascimento \"{c.Texto ?? c.Data?.ToString("dd/MM/yyyy") ?? c.Numero?.ToString()}\" inválido (use dd/mm/aaaa): ficou de fora.");
            return null;
        }
        return data;
    }

    private static string Normalizar(string texto) => ImportacaoProdutosService.Normalizar(texto);

    // ================================================================ gravação

    // Lê e confere a planilha DE NOVO (não confia na prévia da tela) e grava só as linhas sem erro, num SaveChanges só
    // (ou entra tudo, ou nada). Cliente existente: só o que veio preenchido muda (vazio não apaga o que já tinha).
    public async Task<ResultadoImportacaoClientesResponse> ImportarAsync(Stream arquivo, string nomeDoArquivo, string? usuarioId)
    {
        var (linhas, errosGerais, _) = await AnalisarAsync(arquivo, nomeDoArquivo);
        if (errosGerais.Count > 0) throw new ImportacaoException(errosGerais[0]);
        var validas = linhas.Where(l => l.Erros.Count == 0).ToList();
        if (validas.Count == 0) throw new ImportacaoException("Nenhuma linha pode ser importada: corrija os erros e envie de novo.");

        var ids = validas.Where(l => l.ExistenteId is not null).Select(l => l.ExistenteId!.Value).ToList();
        var existentes = await db.Clientes.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        int novos = 0, atualizados = 0;
        foreach (var l in validas)
        {
            Cliente c;
            if (l.ExistenteId is int id) { c = existentes[id]; atualizados++; }
            else { c = new Cliente { CriadoPorId = usuarioId }; db.Clientes.Add(c); novos++; }
            c.Nome = l.Nome!;
            c.Ativo = true;
            c.Cpf = l.Cpf ?? c.Cpf;
            c.Telefone = l.Telefone ?? c.Telefone;
            c.Apelido = l.Apelido ?? c.Apelido;
            c.Email = l.Email ?? c.Email;
            c.Endereco = l.Endereco ?? c.Endereco;
            c.Nascimento = l.Nascimento ?? c.Nascimento;
            c.Observacao = l.Observacao ?? c.Observacao;
            c.Raquete = l.Raquete ?? c.Raquete;
            c.Corda = l.Corda ?? c.Corda;
            c.Tensao = l.Tensao ?? c.Tensao;
        }
        await db.SaveChangesAsync();
        return new(novos, atualizados, linhas.Count - validas.Count);
    }
}
