using System.Diagnostics;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

public record SistemaResponse(
    DateTimeOffset LigadoDesde, string Versao, long MemoriaMb, string Banco, long BancoMb,
    double DiscoLivreGb, double DiscoTotalGb, List<EnderecoNoAr> Enderecos, BackupInfo Backup, List<UsoCliente> UsoPorCliente,
    List<RotinaInfo> Rotinas);

public record EnderecoNoAr(string Endereco, bool NoAr, int? Ms, string? Erro);
public record BackupInfo(string? UltimoLocal, long? UltimoLocalKb, int CopiasLocais, string? Nuvem, bool NuvemOk,
    string? Continuo, bool ContinuoOk);
public record UsoCliente(int Id, string Nome, long Registros, double Percentual);
public record RotinaInfo(string Nome, string Situacao);

// Área do dono → Sistema: a saúde da plataforma num olhar (serviço, banco, disco, backups, endereços no ar) e quanto do
// banco cada cliente ocupa (em REGISTROS, sem olhar o conteúdo).
[ApiController]
[Route("api/plataforma/sistema")]
[DonoDaPlataforma]
public class SistemaController(AppDbContext db, IConfiguration config, IHttpClientFactory http, Services.Simulador.SimuladorEstado simulador) : ControllerBase
{
    [HttpGet]
    public async Task<SistemaResponse> Obter(CancellationToken ct)
    {
        var processo = Process.GetCurrentProcess();
        var conexao = db.Database.GetDbConnection();
        var bancoMb = await db.Database.SqlQuery<long>($"SELECT pg_database_size(current_database()) / 1048576 AS \"Value\"").FirstAsync(ct);
        var disco = new DriveInfo("/");

        // Registros de cada cliente nas tabelas que mais crescem (vendas e seus itens/pagamentos, estoque, financeiro).
        var linhas = await db.Database.SqlQuery<LinhaUso>($"""
            SELECT e."Id" AS "Id", e."Nome" AS "Nome",
              (SELECT count(*) FROM "Vendas" v WHERE v."EmpresaId" = e."Id")
            + (SELECT count(*) FROM "ItensVenda" i JOIN "Vendas" v ON v."Id" = i."VendaId" WHERE v."EmpresaId" = e."Id")
            + (SELECT count(*) FROM "Produtos" p WHERE p."EmpresaId" = e."Id")
            + (SELECT count(*) FROM "Movimentacoes" m WHERE m."EmpresaId" = e."Id")
            + (SELECT count(*) FROM "MovimentosFinanceiros" f WHERE f."EmpresaId" = e."Id") AS "Registros"
            FROM "Empresas" e WHERE NOT e."Plataforma" ORDER BY 3 DESC
            """).ToListAsync(ct);
        var total = Math.Max(1, linhas.Sum(l => l.Registros));

        var enderecos = await Task.WhenAll(
            (config["Plataforma:EnderecosVigiados"] ?? "https://app.wnlabs.com.br/api/saude;https://demo.wnlabs.com.br/api/saude")
            .Split(';', StringSplitOptions.RemoveEmptyEntries).Select(u => TestarAsync(u, ct)));

        return new SistemaResponse(
            new DateTimeOffset(processo.StartTime), typeof(Program).Assembly.GetName().Version?.ToString() ?? "?",
            processo.WorkingSet64 / 1048576, conexao.Database, bancoMb,
            Math.Round(disco.AvailableFreeSpace / 1e9, 1), Math.Round(disco.TotalSize / 1e9, 1),
            enderecos.ToList(), Backups(),
            linhas.Select(l => new UsoCliente(l.Id, l.Nome, l.Registros, Math.Round(100.0 * l.Registros / total, 1))).ToList(),
            [
                new("Vendas automáticas (simulador)", !simulador.Ativo ? "desligado"
                    : $"ligado em {await db.Empresas.CountAsync(e => e.Ativa && e.VendasAutomaticas != ModosVendasAutomaticas.Desligado, ct)} loja(s)"),
                new("Cobranças (mensalidades e bloqueio)", CobrancasTrabalhador.UltimaRodada is { } c
                    ? $"última rodada {TimeZoneInfo.ConvertTime(c, Relogio.Fuso):dd/MM HH:mm}" : "ainda não rodou desde que ligou"),
            ]);
    }

    private async Task<EnderecoNoAr> TestarAsync(string url, CancellationToken ct)
    {
        var cliente = http.CreateClient();
        cliente.Timeout = TimeSpan.FromSeconds(6);
        var relogio = Stopwatch.StartNew();
        try
        {
            var r = await cliente.GetAsync(url, ct);
            return new EnderecoNoAr(new Uri(url).Host, r.IsSuccessStatusCode, (int)relogio.ElapsedMilliseconds, r.IsSuccessStatusCode ? null : $"HTTP {(int)r.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new EnderecoNoAr(new Uri(url).Host, false, null, e is TaskCanceledException ? "não respondeu em 6 s" : e.Message);
        }
    }

    // Backups: pasta diária do banco (~/backup/marketplace-auto/AAAA-MM-DD), o resultado do envio diário para a nuvem e o
    // envio contínuo (WAL, a cada 5 min: infra/enviar-wal.sh → wal-ultimo.txt). Contínuo parado há mais de 15 min = falha.
    private BackupInfo Backups()
    {
        var raiz = config["Plataforma:PastaBackups"] ?? "/home/ubuntu/backup";
        try
        {
            var pastas = Directory.Exists(Path.Combine(raiz, "marketplace-auto"))
                ? Directory.GetDirectories(Path.Combine(raiz, "marketplace-auto")).Order().ToList() : [];
            var ultima = pastas.LastOrDefault();
            var dump = ultima is null ? null : new FileInfo(Path.Combine(ultima, "marketplace.dump"));
            var nuvemArquivo = Path.Combine(raiz, "nuvem-ultimo.txt");
            DateTime? nuvemQuando = System.IO.File.Exists(nuvemArquivo) ? System.IO.File.GetLastWriteTime(nuvemArquivo) : null;
            var nuvemOk = nuvemQuando is not null && System.IO.File.ReadLines(nuvemArquivo).LastOrDefault()?.Trim() == "OK";
            var walArquivo = Path.Combine(raiz, "wal-ultimo.txt");
            DateTime? walQuando = System.IO.File.Exists(walArquivo) ? System.IO.File.GetLastWriteTime(walArquivo) : null;
            var walOk = walQuando is not null && DateTime.Now - walQuando < TimeSpan.FromMinutes(15)
                && System.IO.File.ReadLines(walArquivo).FirstOrDefault()?.Contains(" ok ") == true;
            return new BackupInfo(ultima is null ? null : Path.GetFileName(ultima), dump?.Exists == true ? dump.Length / 1024 : null,
                pastas.Count, nuvemQuando?.ToString("dd/MM/yyyy HH:mm"), nuvemOk, walQuando?.ToString("dd/MM HH:mm"), walOk);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new BackupInfo(null, null, 0, null, false, null, false);
        }
    }

    private record LinhaUso(int Id, string Nome, long Registros);
}
