using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services;

// Regras da ordem de serviço usadas pela tela e pelo bot: o número sequencial da loja e a troca de situação.
public static class OrdensServicoNumeracao
{
    // Grava uma OS nova com o próximo número da loja. Duas ao mesmo tempo: a segunda tenta de novo com o número seguinte.
    public static async Task SalvarNovaAsync(AppDbContext db, OrdemServico ordem)
    {
        for (var tentativa = 0; ; tentativa++)
        {
            ordem.Numero = (await db.OrdensServico.MaxAsync(o => (int?)o.Numero) ?? 0) + 1;
            if (ordem.Id == 0) db.OrdensServico.Add(ordem);
            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (tentativa < 3)
            {
                // número já usado por outra OS gravada agora: tenta o próximo
            }
        }
    }

    // Recebida → Em andamento → Pronta → Entregue (ou Cancelada). Voltar um passo também vale (clique errado).
    public static void Mudar(OrdemServico o, string status)
    {
        var agora = DateTimeOffset.UtcNow;
        o.Status = status;
        o.ProntaEm = status is StatusOrdem.Pronta or StatusOrdem.Entregue ? o.ProntaEm ?? agora : null;
        o.EntregueEm = status == StatusOrdem.Entregue ? o.EntregueEm ?? agora : null;
    }
}
