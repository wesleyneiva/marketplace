using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Simulador;

// A oficina da loja de tênis em teste: quando o bot vende um serviço (encordoamento, troca de grip), ele também abre a
// ORDEM DE SERVIÇO (cliente deixou a raquete), faz o serviço e entrega — como o encordoador faria. Só mexe nas ordens
// que ele mesmo criou (Automatica); as das pessoas ficam com elas.
public static class OficinaDemonstracao
{
    private static readonly string[] Nomes =
    [
        "Ana Paula", "Bruno", "Carla", "Diego", "Eduardo", "Fernanda", "Gabriel", "Helena", "Igor", "Juliana", "Leonardo",
        "Mariana", "Nicolas", "Patrícia", "Rafael", "Renata", "Rodrigo", "Sofia", "Thiago", "Vanessa", "Vinícius", "Luiza",
    ];
    private static readonly string[] Sobrenomes =
        ["Silva", "Souza", "Oliveira", "Costa", "Pereira", "Rodrigues", "Almeida", "Machado", "Rocha", "Fagundes", "Becker", "Schmidt"];
    private static readonly string[] RaquetesDoCliente =
    [
        "Babolat Pure Aero", "Babolat Pure Drive", "Wilson Blade 98", "Wilson Pro Staff 97", "Wilson Clash 100",
        "Head Speed MP", "Head Gravity MP", "Head Radical MP", "Yonex EZONE 100", "Yonex VCORE 98", "Tecnifibre TF40",
    ];

    // Depois da venda: cada serviço de oficina vendido vira uma OS (paga, ligada à venda).
    public static async Task AbrirOrdensAsync(AppDbContext db, Venda venda, IReadOnlyList<ItemVendaRequest> itens,
        IReadOnlyList<Produto> produtos, string usuarioId)
    {
        var porId = produtos.ToDictionary(p => p.Id);
        var servicos = itens.Select(i => porId[i.ProdutoId]).Where(p => p.Servico && EhDaOficina(p.Nome)).ToList();
        if (servicos.Count == 0) return;

        // Corda comprada junto (o cliente leva a corda nova); senão, a corda dele mesmo.
        var corda = itens.Select(i => porId[i.ProdutoId]).FirstOrDefault(p => p.Categoria?.Nome == "Cordas" || p.Nome.StartsWith("Corda "));
        var raqueteNova = itens.Select(i => porId[i.ProdutoId]).FirstOrDefault(p => p.Modelo?.Nome.StartsWith("Raquete") == true);
        var nome = $"{Sortear(Nomes)} {Sortear(Sobrenomes)}";
        var telefone = $"519{Random.Shared.Next(80000000, 99999999)}";
        var agora = DateTimeOffset.UtcNow;

        foreach (var s in servicos)
        {
            var expresso = PerfilConsumo.SemAcento(s.Nome).Contains("expresso");
            var tensao = Random.Shared.Next(21, 27);
            var ordem = new OrdemServico
            {
                Cliente = nome, Telefone = telefone,
                Raquete = raqueteNova is not null ? $"{raqueteNova.Nome} (comprada agora)" : Sortear(RaquetesDoCliente),
                Corda = PerfilConsumo.SemAcento(s.Nome).Contains("encordoamento")
                    ? corda?.Nome ?? "Corda do cliente" : null,
                Tensao = PerfilConsumo.SemAcento(s.Nome).Contains("encordoamento") ? $"{tensao}/{tensao - Random.Shared.Next(0, 3)} lb" : null,
                Servico = s.Nome,
                Valor = itens.Where(i => i.ProdutoId == s.Id || i.ProdutoId == corda?.Id).Sum(i => i.Quantidade * porId[i.ProdutoId].PrecoVenda),
                Paga = true, VendaId = venda.Id, Status = StatusOrdem.Recebida, Automatica = true, CriadaPorId = usuarioId,
                Prazo = expresso ? agora.AddHours(1) : agora.AddHours(Random.Shared.Next(4, 26)),
            };
            await OrdensServicoNumeracao.SalvarNovaAsync(db, ordem);
        }
    }

    // A cada minuto com a loja aberta: o encordoador começa, termina e o cliente vem buscar.
    public static async Task AndarComAsOrdensAsync(AppDbContext db)
    {
        var agora = DateTimeOffset.UtcNow;
        var abertas = await db.OrdensServico
            .Where(o => o.Automatica && (o.Status == StatusOrdem.Recebida || o.Status == StatusOrdem.EmAndamento || o.Status == StatusOrdem.Pronta))
            .ToListAsync();
        foreach (var o in abertas)
        {
            var expresso = PerfilConsumo.SemAcento(o.Servico).Contains("expresso");
            switch (o.Status)
            {
                case StatusOrdem.Recebida when agora - o.CriadaEm > TimeSpan.FromMinutes(expresso ? 5 : 40) && Random.Shared.NextDouble() < (expresso ? 0.5 : 0.05):
                    OrdensServicoNumeracao.Mudar(o, StatusOrdem.EmAndamento);
                    break;
                case StatusOrdem.EmAndamento when agora - o.CriadaEm > TimeSpan.FromMinutes(expresso ? 35 : 90) && Random.Shared.NextDouble() < (expresso ? 0.3 : 0.04):
                    OrdensServicoNumeracao.Mudar(o, StatusOrdem.Pronta);
                    break;
                case StatusOrdem.Pronta when agora - o.ProntaEm > TimeSpan.FromMinutes(expresso ? 10 : 120) && Random.Shared.NextDouble() < (expresso ? 0.2 : 0.006):
                    OrdensServicoNumeracao.Mudar(o, StatusOrdem.Entregue);
                    break;
            }
        }
        await db.SaveChangesAsync();
    }

    public static bool EhDaOficina(string nome)
    {
        var n = PerfilConsumo.SemAcento(nome);
        return n.Contains("encordoamento") || n.Contains("grip") || n.Contains("stencil");
    }

    private static string Sortear(string[] lista) => lista[Random.Shared.Next(lista.Length)];
}
