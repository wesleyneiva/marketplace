using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Configurações da PRÓPRIA empresa (de quem está logado). Ler: todos; mudar: só o Administrador dela.
[ApiController]
[Route("api/empresa/configuracao")]
[Authorize]
public class EmpresaController(AppDbContext db, ContextoEmpresa contexto, LocaisDasLojas locais, ColetorClima coletor, IHttpClientFactory http) : ControllerBase
{
    [HttpGet]
    public async Task<ConfiguracaoEmpresaResponse> Obter() => Resposta(await CarregarAsync());

    [HttpPut]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<ActionResult<ConfiguracaoEmpresaResponse>> Salvar(ConfiguracaoEmpresaRequest r)
    {
        if (r.BalancaEtiqueta is not (EtiquetaBalancaTipos.Preco or EtiquetaBalancaTipos.Peso))
            return BadRequest(new { mensagem = "Etiqueta da balança: Preco ou Peso." });
        var empresa = await CarregarAsync();
        empresa.BalancaDigitosCodigo = r.BalancaDigitosCodigo;
        empresa.BalancaEtiqueta = r.BalancaEtiqueta;
        await db.SaveChangesAsync();
        return Resposta(empresa);
    }

    // GET /api/empresa/configuracao/cidades?busca=manaus → cidades do Brasil (com o fuso de cada uma)
    [HttpGet("cidades")]
    public async Task<ActionResult<List<CidadeResponse>>> Cidades(string? busca, CancellationToken ct)
    {
        var termo = busca?.Trim() ?? "";
        if (termo.Length < 2) return new List<CidadeResponse>();
        try
        {
            return await Geografia.BuscarCidadesAsync(http.CreateClient(), termo, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { mensagem = "A busca de cidades não respondeu. Tente de novo." });
        }
    }

    // PUT /api/empresa/configuracao/cidade → onde fica a loja (clima + fuso horário)
    [HttpPut("cidade")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<ActionResult<ConfiguracaoEmpresaResponse>> SalvarCidade(CidadeRequest r)
    {
        if (!Geografia.FusoValido(r.Fuso)) return BadRequest(new { mensagem = "Fuso horário desconhecido." });
        var empresa = await CarregarAsync();
        empresa.Cidade = r.Nome.Trim();
        empresa.Uf = r.Uf.Trim().ToUpperInvariant();
        empresa.Latitude = Math.Round(r.Latitude, 5);
        empresa.Longitude = Math.Round(r.Longitude, 5);
        empresa.Fuso = r.Fuso;
        await db.SaveChangesAsync();
        locais.Esquecer(empresa.Id);
        coletor.Acordar(); // busca o clima da cidade nova (e os últimos 60 dias) agora
        Relogio.UsarFuso(empresa.Fuso); // a resposta já sai com a hora da cidade nova
        return Resposta(empresa);
    }

    // PUT /api/empresa/configuracao/horario → dias e horas em que a loja abre, feriados
    [HttpPut("horario")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<ActionResult<ConfiguracaoEmpresaResponse>> SalvarHorario(HorarioFuncionamento r)
    {
        if (ValidarHorario(r) is { } erro) return BadRequest(new { mensagem = erro });
        var empresa = await CarregarAsync();
        empresa.Horario = Normalizar(r);
        await db.SaveChangesAsync();
        locais.Esquecer(empresa.Id);
        locais.Aplicar(empresa.Id); // a resposta já sai com o horário novo
        return Resposta(empresa);
    }

    private static readonly string[] NomesDias = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

    // Dia fechado guarda o horário padrão; feriados da cidade em ordem do calendário. (Também usado pela área do dono.)
    public static HorarioFuncionamento Normalizar(HorarioFuncionamento r) => new()
    {
        Dias = r.Dias.Select(d => d.Aberto ? d : d with { Abre = new TimeOnly(8, 0), Fecha = new TimeOnly(19, 0) }).ToList(),
        Feriados = r.Feriados,
        FeriadosLocais = r.FeriadosLocais.Select(f => f with { Nome = f.Nome.Trim() }).OrderBy(f => f.Mes).ThenBy(f => f.Dia).ToList(),
    };

    public static string? ValidarHorario(HorarioFuncionamento h)
    {
        if (h.Dias is not { Count: 7 }) return "Informe o horário dos 7 dias da semana.";
        if (h.Dias.All(d => !d.Aberto)) return "A loja precisa abrir em pelo menos um dia da semana.";
        for (var i = 0; i < 7; i++)
            if (h.Dias[i] is { Aberto: true } d && d.Fecha <= d.Abre)
                return $"{NomesDias[i]}: a hora de fechar precisa ser depois da hora de abrir (depois da meia-noite não dá).";
        if (h.Feriados is not (RegrasFeriado.Fechado or RegrasFeriado.Domingo or RegrasFeriado.Normal))
            return "Regra dos feriados: Fechado, Domingo ou Normal.";
        if (h.FeriadosLocais is null || h.FeriadosLocais.Count > 30) return "No máximo 30 feriados da cidade.";
        foreach (var f in h.FeriadosLocais)
        {
            if (string.IsNullOrWhiteSpace(f.Nome) || f.Nome.Trim().Length > 60) return "Cada feriado da cidade precisa de um nome (até 60 letras).";
            if (f.Mes is < 1 or > 12 || f.Dia < 1 || f.Dia > DateTime.DaysInMonth(2024, f.Mes)) return $"Data inválida no feriado \"{f.Nome.Trim()}\".";
        }
        if (h.FeriadosLocais.GroupBy(f => (f.Dia, f.Mes)).Any(g => g.Count() > 1)) return "Há dois feriados da cidade no mesmo dia.";
        return null;
    }

    // Os feriados dos próximos 12 meses (nacionais + da cidade), para a tela mostrar o que vai acontecer.
    private static List<ProximoFeriado> ProximosFeriados()
    {
        var hoje = Relogio.Hoje;
        return Enumerable.Range(0, 366).Select(hoje.AddDays)
            .Select(d => (Dia: d, Nome: Expediente.Feriado(d)))
            .Where(x => x.Nome is not null)
            .Take(6)
            .Select(x => new ProximoFeriado(x.Dia, x.Nome!, Expediente.DoDia(x.Dia) is { } h
                ? $"abre {Expediente.Hora(h.Abre)}–{Expediente.Hora(h.Fecha)}" : "fechado"))
            .ToList();
    }

    // GET /api/empresa/configuracao/assinatura → a mensalidade do sistema (aviso no topo, só para o Administrador)
    [HttpGet("assinatura")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<SituacaoAssinaturaResponse> Assinatura([FromServices] CobrancasService cobrancas)
    {
        var hoje = Relogio.Hoje;
        var cfg = await cobrancas.ConfiguracaoAsync();
        var abertas = (await db.Faturas.AsNoTracking()
                .Where(f => f.ClienteId == contexto.EmpresaId && f.Status == StatusFatura.Aberta).OrderBy(f => f.Vencimento).ToListAsync())
            .Select(f => new FaturaDoCliente(f.Competencia, f.Vencimento, f.Valor, f.Descricao, CobrancasService.DiasDeAtraso(f, hoje))).ToList();
        string? nivel = null, mensagem = null;
        if (abertas.FirstOrDefault(f => f.DiasAtraso > 0) is { } atrasada)
        {
            var faltam = cfg.DiasParaBloquear - atrasada.DiasAtraso;
            nivel = "perigo";
            mensagem = $"Mensalidade do sistema em atraso há {atrasada.DiasAtraso} dia(s)."
                + (cfg.BloqueioAutomatico ? faltam > 0 ? $" O acesso será suspenso em {faltam} dia(s) se não for paga." : " O acesso pode ser suspenso a qualquer momento." : "");
        }
        else if (abertas.FirstOrDefault(f => f.Vencimento <= hoje.AddDays(cfg.DiasAvisoAntes)) is { } proxima)
        {
            var dias = proxima.Vencimento.DayNumber - hoje.DayNumber;
            nivel = "info";
            mensagem = $"Mensalidade do sistema ({proxima.Valor.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}) vence {(dias == 0 ? "hoje" : dias == 1 ? "amanhã" : $"em {dias} dias")}, {proxima.Vencimento:dd/MM}.";
        }
        return new SituacaoAssinaturaResponse(nivel, mensagem, abertas, cfg.ChavePix, cfg.NomeRecebedor, Documentos.Telefone(cfg.ContatoCobranca));
    }

    private Task<Empresa> CarregarAsync() => db.Empresas.FirstAsync(e => e.Id == contexto.EmpresaId);

    // Exemplo para a tela mostrar como fica a etiqueta: produto 42, R$ 15,99 (ou 1,235 kg).
    private static ConfiguracaoEmpresaResponse Resposta(Empresa e) => new(e.BalancaDigitosCodigo, e.BalancaEtiqueta,
        EtiquetaBalanca.Montar(42, e.BalancaEtiqueta == EtiquetaBalancaTipos.Peso ? 1235 : 1599, e.BalancaDigitosCodigo),
        e.Cidade, e.Uf, e.Fuso, Relogio.Agora.ToString("dd/MM/yyyy HH:mm"), e.Horario, Expediente.Descricao(), ProximosFeriados());
}
