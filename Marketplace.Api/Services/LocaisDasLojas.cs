using System.Collections.Concurrent;
using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Onde fica cada loja (cidade + coordenadas + fuso). Lat/lon arredondadas em 2 casas (~1 km) para o clima:
// lojas na mesma cidade compartilham as mesmas linhas da tabela Clima.
public record LocalLoja(string Cidade, string? Uf, decimal Latitude, decimal Longitude, string Fuso)
{
    public decimal LatitudeClima => Math.Round(Latitude, 2);
    public decimal LongitudeClima => Math.Round(Longitude, 2);
    public string Nome => Uf is null ? Cidade : $"{Cidade}/{Uf}";
}

// Cache (5 minutos) do local de cada empresa: o relógio consulta a cada requisição, não dá para ir ao banco toda vez.
// Singleton; lê o banco num escopo próprio (a tabela Empresas não tem filtro de empresa).
public class LocaisDasLojas(IServiceScopeFactory escopos)
{
    private record Item(LocalLoja? Local, string Fuso, DateTime LidoEm);
    private readonly ConcurrentDictionary<int, Item> _cache = new();

    public LocalLoja? Local(int empresaId) => Obter(empresaId).Local;
    public string Fuso(int empresaId) => Obter(empresaId).Fuso;

    public void Esquecer(int empresaId) => _cache.TryRemove(empresaId, out _);

    private Item Obter(int empresaId)
    {
        if (_cache.TryGetValue(empresaId, out var item) && DateTime.UtcNow - item.LidoEm < TimeSpan.FromMinutes(5))
            return item;

        using var scope = escopos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var e = db.Empresas.AsNoTracking().Where(x => x.Id == empresaId)
            .Select(x => new { x.Cidade, x.Uf, x.Latitude, x.Longitude, x.Fuso }).FirstOrDefault();
        var local = e is { Cidade: not null, Latitude: not null, Longitude: not null }
            ? new LocalLoja(e.Cidade, e.Uf, e.Latitude.Value, e.Longitude.Value, e.Fuso) : null;
        item = new Item(local, e?.Fuso ?? Relogio.FusoPadrao, DateTime.UtcNow);
        _cache[empresaId] = item;
        return item;
    }
}
