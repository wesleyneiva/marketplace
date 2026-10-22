using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Simulador;

// Clientes FIXOS da loja de esporte em teste: ~60 pessoas que voltam ao longo das semanas (os "de sempre" mais vezes),
// para o cadastro e o histórico terem vida. Parte das vendas sai no nome de um deles; toda OS da oficina também
// (o encordoamento é de quem deixou a raquete). Cada um tem a raquete, a corda e a tensão dele.
public static class ClientesSimulados
{
    public const int Quantos = 60;
    private const int DeSempre = 20;        // os 20 primeiros voltam bem mais
    public const double ChanceIdentificado = 0.45; // venda sem oficina: 45% dão o nome/CPF

    private static readonly string[] Cordas =
        ["Babolat RPM Blast 1.25", "Luxilon ALU Power 1.25", "Solinco Hyper-G 1.25", "Babolat Xcel 1.30", "Wilson NXT 1.30", "Head Lynx 1.25"];

    // Garante a turma (cria o que falta) e sorteia um. Devolve null se a loja não tiver clientes simulados ainda e não der.
    public static async Task<Cliente> SortearAsync(AppDbContext db, string usuarioId, CancellationToken ct)
    {
        var turma = await db.Clientes.Where(c => c.Simulado && c.Ativo).OrderBy(c => c.Id).ToListAsync(ct);
        if (turma.Count < Quantos)
        {
            var telefones = turma.Select(c => c.Telefone).ToHashSet();
            for (var i = turma.Count; i < Quantos; i++)
            {
                string telefone;
                do telefone = $"519{Random.Shared.Next(80000000, 99999999)}"; while (!telefones.Add(telefone));
                var tensao = Random.Shared.Next(21, 27);
                var cliente = new Cliente
                {
                    Nome = $"{Sortear(OficinaDemonstracao.Nomes)} {Sortear(OficinaDemonstracao.Sobrenomes)}",
                    Telefone = telefone, Simulado = true, CriadoPorId = usuarioId,
                    Nascimento = Random.Shared.NextDouble() < 0.6
                        ? new DateOnly(Random.Shared.Next(1965, 2008), Random.Shared.Next(1, 13), Random.Shared.Next(1, 29)) : null,
                    Raquete = Random.Shared.NextDouble() < 0.75 ? Sortear(OficinaDemonstracao.RaquetesDoCliente) : null,
                };
                if (cliente.Raquete is not null)
                {
                    cliente.Corda = Sortear(Cordas);
                    cliente.Tensao = $"{tensao}/{tensao - Random.Shared.Next(0, 3)} lbs";
                }
                db.Clientes.Add(cliente);
                turma.Add(cliente);
            }
            await db.SaveChangesAsync(ct);
        }
        // 65% das vezes é um "de sempre"; senão, qualquer um.
        return Random.Shared.NextDouble() < 0.65 ? turma[Random.Shared.Next(Math.Min(DeSempre, turma.Count))] : turma[Random.Shared.Next(turma.Count)];
    }

    // Quem atendeu: um dos vendedores ativos (a TopSpin tem um só); sem vendedor cadastrado, a venda fica sem.
    public static async Task<int?> VendedorAsync(AppDbContext db, CancellationToken ct)
    {
        var ids = await db.Vendedores.Where(v => v.Ativo).Select(v => v.Id).ToListAsync(ct);
        return ids.Count == 0 ? null : ids[Random.Shared.Next(ids.Count)];
    }

    private static string Sortear(string[] lista) => lista[Random.Shared.Next(lista.Length)];
}
