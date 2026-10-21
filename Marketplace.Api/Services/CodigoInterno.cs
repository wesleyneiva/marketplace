using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Código de barras INTERNO para produto sem EAN do fornecedor (variações da grade, produtos da própria loja):
// EAN-13 "20" + 10 dígitos sequenciais + dígito verificador. A faixa 20–29 é de uso interno da loja no padrão GS1
// (nenhum fabricante usa), então não bate com código de produto de verdade. A etiqueta de balança também começa com
// 2, mas o caixa procura primeiro o código exato de um produto (ProdutosController.LerEtiquetaBalanca).
public static class CodigoInterno
{
    public const string Prefixo = "20";

    // Próximos "quantos" códigos livres desta empresa (o filtro global já limita à empresa atual).
    public static async Task<Queue<string>> ReservarAsync(AppDbContext db, int quantos)
    {
        var existentes = await db.Produtos.IgnoreQueryFilters().Where(p => p.EmpresaId == db.EmpresaAtual && p.CodigoBarras != null
                && p.CodigoBarras.StartsWith(Prefixo) && p.CodigoBarras.Length == 13)
            .Select(p => p.CodigoBarras!).ToListAsync();
        var maior = existentes.Select(c => long.TryParse(c[2..12], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        var fila = new Queue<string>();
        for (var i = 1; i <= quantos; i++)
        {
            var corpo = Prefixo + (maior + i).ToString("D10");
            fila.Enqueue(corpo + Digito(corpo));
        }
        return fila;
    }

    // Dígito verificador do EAN-13: pesos 1 e 3 alternados da esquerda para a direita.
    public static int Digito(string doze)
    {
        var soma = 0;
        for (var i = 0; i < 12; i++) soma += (doze[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - soma % 10) % 10;
    }
}
