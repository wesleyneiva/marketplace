using System.Text.Json;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// IMPLANTAÇÃO do Financeiro de um cliente pela linha de comando da VM, a partir de um arquivo .json:
// saldos iniciais das contas (gaveta, cofre, banco), contas fixas do mês (repetidas por N meses), saldo mínimo de
// segurança e alíquota do Simples. Pode rodar de novo: conta fixa que já existe (mesma descrição) não é duplicada.
//
//   ./implantar-financeiro.sh --arquivo ~/juvenal-financeiro.json            (prévia, não grava)
//   ./implantar-financeiro.sh --arquivo ~/juvenal-financeiro.json --gravar true
public static class ImplantarFinanceiroCli
{
    public const string Comando = "implantar-financeiro";

    private record Arquivo(int Empresa, DateOnly Data, List<ContaArquivo> Contas, decimal? SaldoMinimo, decimal? AliquotaSimples,
        List<FixaArquivo> ContasFixas);
    private record ContaArquivo(string Chave, string? Nome, string? Banco, decimal Saldo);
    private record FixaArquivo(string Descricao, string Categoria, decimal Valor, int Dia, int Meses, string? Observacao);

    public static async Task<int> ExecutarAsync(IServiceProvider services, IConfiguration args)
    {
        var caminho = args["arquivo"];
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
        {
            Console.Error.WriteLine("❌ Uso: implantar-financeiro --arquivo /caminho/financeiro.json [--gravar true]");
            return 1;
        }
        var a = JsonSerializer.Deserialize<Arquivo>(await File.ReadAllTextAsync(caminho), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Arquivo vazio.");
        var gravar = args.GetValue("gravar", false);

        using var scope = services.CriarEscopo(a.Empresa);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == a.Empresa && !e.Plataforma);
        if (empresa is null) { Console.Error.WriteLine($"❌ Empresa #{a.Empresa} não existe."); return 1; }
        Console.WriteLine($"\n🏦 Financeiro de #{empresa.Id} {empresa.Nome} — implantação em {a.Data:dd/MM/yyyy}");

        // 1) Contas (pela chave do sistema: dinheiro, cofre, banco)
        var contas = await db.ContasFinanceiras.ToListAsync();
        foreach (var c in a.Contas)
        {
            var conta = contas.FirstOrDefault(x => x.Chave == c.Chave);
            if (conta is null) { Console.Error.WriteLine($"❌ Conta com chave \"{c.Chave}\" não existe."); return 1; }
            Console.WriteLine($"   Conta {conta.Nome}{(c.Nome is null || c.Nome == conta.Nome ? "" : $" → {c.Nome}")}: saldo inicial {Reais(c.Saldo)} em {a.Data:dd/MM}");
            if (c.Nome is not null) conta.Nome = c.Nome;
            if (c.Banco is not null) conta.Banco = c.Banco;
            (conta.SaldoInicial, conta.SaldoInicialEm) = (c.Saldo, a.Data);
        }

        if (a.SaldoMinimo is { } minimo) { empresa.SaldoMinimoCaixa = minimo; Console.WriteLine($"   Saldo mínimo de segurança: {Reais(minimo)}"); }
        if (a.AliquotaSimples is { } aliquota) { empresa.AliquotaSimples = aliquota; Console.WriteLine($"   Alíquota do Simples: {aliquota:0.##}%"); }

        // 2) Contas fixas: N meses a partir do primeiro vencimento depois da implantação
        var categorias = await db.CategoriasFinanceiras.ToListAsync();
        var existentes = await db.ContasPagar.Select(c => c.Descricao).ToListAsync();
        decimal porMes = 0;
        foreach (var f in a.ContasFixas)
        {
            var categoria = categorias.FirstOrDefault(c => c.Nome == f.Categoria);
            if (categoria is null) { Console.Error.WriteLine($"❌ Categoria \"{f.Categoria}\" não existe no plano de contas."); return 1; }
            if (existentes.Any(d => d.StartsWith(f.Descricao + " (") || d == f.Descricao))
            {
                Console.WriteLine($"   = {f.Descricao}: já lançada (não duplica)");
                continue;
            }
            var dia = Math.Clamp(f.Dia, 1, 28);
            var primeiro = new DateOnly(a.Data.Year, a.Data.Month, dia);
            if (primeiro <= a.Data) primeiro = primeiro.AddMonths(1);
            porMes += f.Valor;
            Console.WriteLine($"   + {f.Descricao}: {Reais(f.Valor)} todo dia {dia}, {f.Meses} meses a partir de {primeiro:dd/MM/yyyy} ({f.Categoria})");
            for (var i = 0; i < f.Meses; i++)
                db.ContasPagar.Add(new ContaPagar
                {
                    Descricao = f.Meses > 1 ? $"{f.Descricao} ({i + 1}/{f.Meses})" : f.Descricao,
                    Vencimento = primeiro.AddMonths(i), Valor = f.Valor, CategoriaFinanceira = categoria,
                    Observacao = f.Observacao ?? "Conta fixa (implantação)",
                });
        }
        Console.WriteLine($"   Contas fixas novas: {Reais(porMes)} por mês");

        if (!gravar)
        {
            Console.WriteLine("\n   (só a prévia — nada foi gravado. Para gravar: --gravar true)\n");
            return 0;
        }
        await db.SaveChangesAsync();
        Console.WriteLine("\n✅ Financeiro implantado.\n");
        return 0;
    }

    private static string Reais(decimal v) => v.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
