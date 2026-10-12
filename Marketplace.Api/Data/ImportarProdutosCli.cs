using Marketplace.Api.Seguranca;
using Marketplace.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// IMPLANTAÇÃO pela linha de comando da VM: importa a planilha de produtos (com estoque inicial e fornecedores)
// direto na empresa de um cliente — o mesmo código da tela Produtos → Importar planilha.
//
//   ./importar-produtos.sh --empresa 4 --arquivo ~/planilha.csv            (só mostra a prévia, não grava)
//   ./importar-produtos.sh --empresa 4 --arquivo ~/planilha.csv --gravar true
public static class ImportarProdutosCli
{
    public const string Comando = "importar-produtos";

    public static async Task<int> ExecutarAsync(IServiceProvider services, IConfiguration args)
    {
        var empresaId = args.GetValue<int?>("empresa");
        var arquivo = args["arquivo"];
        if (empresaId is null || string.IsNullOrWhiteSpace(arquivo) || !File.Exists(arquivo))
        {
            Console.Error.WriteLine("❌ Uso: importar-produtos --empresa 4 --arquivo /caminho/planilha.csv [--gravar true]");
            return 1;
        }

        using var scope = services.CriarEscopo(empresaId.Value);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var empresa = await db.Empresas.Where(e => e.Id == empresaId && !e.Plataforma).Select(e => e.Nome).FirstOrDefaultAsync();
        if (empresa is null)
        {
            Console.Error.WriteLine($"❌ Empresa #{empresaId} não existe.");
            return 1;
        }
        var importacao = scope.ServiceProvider.GetRequiredService<ImportacaoProdutosService>();
        var nome = Path.GetFileName(arquivo);

        await using (var leitura = File.OpenRead(arquivo))
        {
            var p = await importacao.GerarPreviaAsync(leitura, nome);
            Console.WriteLine($"\n📋 Prévia para #{empresaId} {empresa}: {p.TotalLinhas} linhas · {p.Novos} novos · {p.Atualizados} a atualizar · {p.ComErro} com erro");
            if (p.CategoriasNovas.Count > 0) Console.WriteLine($"   Categorias novas: {string.Join(", ", p.CategoriasNovas)}");
            if (p.FornecedoresNovos.Count > 0) Console.WriteLine($"   Fornecedores novos: {string.Join(", ", p.FornecedoresNovos)}");
            foreach (var erro in p.ErrosGerais) Console.WriteLine($"   ❌ {erro}");
            foreach (var l in p.Linhas.Where(l => l.Erros.Count > 0).Take(20))
                Console.WriteLine($"   ❌ linha {l.Linha} ({l.Nome}): {string.Join(" ", l.Erros)}");
            if (p.ErrosGerais.Count > 0) return 1;
        }

        if (!args.GetValue("gravar", false))
        {
            Console.WriteLine("\n   (só a prévia — nada foi gravado. Para gravar: --gravar true)\n");
            return 0;
        }

        await using (var leitura = File.OpenRead(arquivo))
        {
            var r = await importacao.ImportarAsync(leitura, nome, usuarioId: null);
            Console.WriteLine($"\n✅ Gravado em #{empresaId} {empresa}: {r.Novos} produtos novos, {r.Atualizados} atualizados, " +
                $"{r.ProdutosComEstoqueInicial} com estoque inicial, {r.Ignorados} linha(s) com erro ignorada(s).");
            if (r.FornecedoresCriados.Count > 0) Console.WriteLine($"   Fornecedores criados: {string.Join(", ", r.FornecedoresCriados)}");
            if (r.CategoriasCriadas.Count > 0) Console.WriteLine($"   Categorias criadas: {string.Join(", ", r.CategoriasCriadas)}\n");
        }
        return 0;
    }
}
