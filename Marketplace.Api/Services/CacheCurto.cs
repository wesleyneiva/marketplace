using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Marketplace.Api.Services;

// Guarda por pouco tempo (2 min) o resultado das telas pesadas de cada empresa: relatórios e DRE somam centenas de
// milhares de itens. Com vários gerentes ou abas abrindo a mesma tela, o banco faz a conta uma vez só, e quem pede
// enquanto ela roda espera a mesma conta em vez de começar outra (teste de estresse de 19/10/2026).
// A DRE leva a "versão do financeiro" na chave: qualquer gravação que muda o resultado (lançamento, conta a pagar,
// perda, venda cancelada…) troca a versão, e a próxima abertura já calcula de novo (ver AppDbContext.SaveChanges).
public class CacheCurto(IMemoryCache cache)
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(2);

    private static readonly ConcurrentDictionary<int, long> versoesFinanceiro = new();
    public static void FinanceiroMudou(int empresaId) => versoesFinanceiro.AddOrUpdate(empresaId, 1, (_, v) => v + 1);
    public static long VersaoFinanceiro(int empresaId) => versoesFinanceiro.GetValueOrDefault(empresaId);

    public async Task<T> ObterAsync<T>(string chave, Func<Task<T>> calcular)
    {
        var conta = cache.GetOrCreate(chave, e =>
        {
            e.AbsoluteExpirationRelativeToNow = Validade;
            return new Lazy<Task<T>>(calcular); // Lazy: duas chamadas ao mesmo tempo → uma conta só
        })!;
        try { return await conta.Value; }
        catch
        {
            cache.Remove(chave); // erro não fica guardado: a próxima abertura tenta de novo
            throw;
        }
    }
}
