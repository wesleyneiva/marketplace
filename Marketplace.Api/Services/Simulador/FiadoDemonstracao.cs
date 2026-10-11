using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Simulador;

// A caderneta do mercadinho de DEMONSTRAÇÃO: 25 clientes FICTÍCIOS (sem telefone, de propósito: o botão "Cobrar no
// WhatsApp" do demo nunca pode mandar mensagem para um número real), cada um com um jeito de pagar:
//   em dia (paga no vencimento ou até 3 dias depois), costuma atrasar (12 a 20 dias), não paga (calote → baixa em 45 dias)
//   e bloqueado (não compra). Cerca de 3% das compras são no fiado.
public class FiadoDemonstracao(AppDbContext db, FiadoService fiado)
{
    public const string Marca = "Cliente fictício (demonstração)";
    public const double ChanceFiado = 0.03;
    public const int DiasParaBaixa = 45;

    public enum Perfil { EmDia, Atrasa, Calote, Bloqueado }

    private static readonly (string Nome, string? Apelido, decimal Limite, int Dia, Perfil Perfil)[] Lista =
    [
        ("Maria Aparecida Souza", "Dona Maria do 302", 400, 10, Perfil.EmDia),
        ("João Batista Pereira", "Seu João da oficina", 500, 5, Perfil.EmDia),
        ("Ana Lúcia Ferreira", "Dona Ana", 300, 10, Perfil.EmDia),
        ("Carlos Eduardo Lima", "Carlão", 250, 5, Perfil.Atrasa),
        ("Francisca Oliveira", "Dona Chica", 350, 15, Perfil.EmDia),
        ("Antônio Carlos Rocha", "Seu Antônio", 600, 10, Perfil.EmDia),
        ("Juliana Martins", null, 200, 20, Perfil.EmDia),
        ("Pedro Henrique Alves", "Pedrinho do bar", 300, 5, Perfil.Atrasa),
        ("Rosângela Dias", "Dona Rô", 450, 10, Perfil.EmDia),
        ("Sebastião Gomes", "Seu Tião", 350, 5, Perfil.EmDia),
        ("Luciana Barbosa", null, 250, 15, Perfil.EmDia),
        ("Marcos Vinícius Ribeiro", "Marquinhos", 200, 20, Perfil.Atrasa),
        ("Terezinha Castro", "Dona Teté", 500, 10, Perfil.EmDia),
        ("Raimundo Nonato Silva", "Seu Raimundo", 300, 5, Perfil.EmDia),
        ("Patrícia Moreira", null, 200, 10, Perfil.EmDia),
        ("Gilberto Cardoso", "Seu Gil", 400, 15, Perfil.Atrasa),
        ("Cláudia Nascimento", "Dona Cláudia da creche", 300, 10, Perfil.EmDia),
        ("Fernando Teixeira", null, 150, 5, Perfil.EmDia),
        ("Irene Fonseca", "Dona Irene", 350, 20, Perfil.EmDia),
        ("Rogério Mendes", "Rogerinho", 200, 10, Perfil.Calote),
        ("Vera Lúcia Pinto", "Dona Vera", 450, 5, Perfil.EmDia),
        ("Alexandre Duarte", null, 250, 15, Perfil.Atrasa),
        ("Neusa Carvalho", "Dona Neusa", 300, 10, Perfil.EmDia),
        ("Wanderley Prado", "Seu Wanderley", 300, 5, Perfil.Bloqueado),
        ("Simone Araújo", null, 200, 20, Perfil.EmDia),
    ];

    public static Perfil PerfilDe(Cliente c) =>
        c.Bloqueado ? Perfil.Bloqueado
        : c.Observacao?.Contains("não paga") == true ? Perfil.Calote
        : c.Observacao?.Contains("costuma atrasar") == true ? Perfil.Atrasa
        : Perfil.EmDia;

    // Quantos dias depois do vencimento este cliente costuma aparecer para pagar (fixo por cliente: o mesmo "jeito" sempre).
    public static int DiasDepoisDoVencimento(Cliente c) => PerfilDe(c) switch
    {
        Perfil.EmDia => c.Id % 4,          // 0 a 3 dias
        Perfil.Atrasa => 12 + c.Id % 9,    // 12 a 20 dias
        _ => int.MaxValue,                 // nunca
    };

    public async Task<List<Cliente>> GarantirClientesAsync(string? usuarioId)
    {
        var existentes = await db.Clientes.Where(c => c.Observacao != null && c.Observacao.StartsWith(Marca)).ToListAsync();
        if (existentes.Count > 0) return existentes;
        var novos = Lista.Select(x => new Cliente
        {
            Nome = x.Nome, Apelido = x.Apelido, LimiteCredito = x.Limite, DiaPagamento = x.Dia, Bloqueado = x.Perfil == Perfil.Bloqueado,
            CriadoPorId = usuarioId,
            Observacao = Marca + x.Perfil switch
            {
                Perfil.Atrasa => " · costuma atrasar", Perfil.Calote => " · não paga (vai virar baixa)",
                Perfil.Bloqueado => " · bloqueado por atrasos antigos", _ => " · paga em dia",
            },
        }).ToList();
        db.Clientes.AddRange(novos);
        await db.SaveChangesAsync();
        return novos;
    }

    // AO VIVO: este cliente do simulador compra no fiado? (nulo = paga normal). Respeita as regras do caixa (sem gerente).
    public async Task<int?> EscolherClienteAsync(decimal total)
    {
        if (Random.Shared.NextDouble() >= ChanceFiado) return null;
        var clientes = await db.Clientes.AsNoTracking()
            .Where(c => c.Ativo && !c.Bloqueado && c.Observacao != null && c.Observacao.StartsWith(Marca)).ToListAsync();
        if (clientes.Count == 0) return null;
        var situacoes = await fiado.SituacoesAsync(clientes.Select(c => c.Id).ToList());
        var podem = clientes.Where(c => PerfilDe(c) != Perfil.Calote // o calote já "sumiu" da loja
            && FiadoService.MotivoParaGerente(c, situacoes.GetValueOrDefault(c.Id) ?? new SituacaoFiado(0, 0, 0, null, 0, null, null), total) is null).ToList();
        return podem.Count == 0 ? null : podem[Random.Shared.Next(podem.Count)].Id;
    }

    // AO VIVO: com o caixa aberto, os clientes cuja dívida "chegou no dia deles" vêm pagar (dinheiro ou PIX).
    public async Task<int> ReceberDoDiaAsync(SessaoCaixa sessao, DateOnly hoje, string usuarioId)
    {
        var clientes = await db.Clientes.AsNoTracking().Where(c => c.Observacao != null && c.Observacao.StartsWith(Marca)).ToListAsync();
        var abertos = (await fiado.EmAbertoAsync()).Where(a => clientes.Any(c => c.Id == a.ClienteId)).ToList();
        var pagos = 0;
        foreach (var c in clientes)
        {
            var atraso = DiasDepoisDoVencimento(c);
            if (atraso == int.MaxValue) continue;
            var devido = abertos.Where(a => a.ClienteId == c.Id && a.Vencimento.AddDays(atraso) <= hoje).Sum(a => a.Aberto);
            if (devido <= 0) continue;
            db.LancamentosFiado.Add(new LancamentoFiado
            {
                ClienteId = c.Id, Tipo = TipoLancamentoFiado.Pagamento, Valor = devido, SessaoCaixaId = sessao.Id, UsuarioId = usuarioId,
                Forma = Random.Shared.NextDouble() < 0.6 ? FormaPagamento.Dinheiro : FormaPagamento.Pix,
                Observacao = $"Recebido no Caixa {sessao.NumeroCaixa}",
            });
            pagos++;
        }
        await db.SaveChangesAsync();
        return pagos;
    }

    // Calote: passou de 45 dias do vencimento sem pagar → o dono dá baixa (vira Inadimplência na DRE).
    public async Task<int> BaixarCalotesAsync(DateOnly hoje, string? usuarioId)
    {
        var clientes = await db.Clientes.AsNoTracking().Where(c => c.Observacao != null && c.Observacao.StartsWith(Marca)).ToListAsync();
        var vencidos = (await fiado.EmAbertoAsync())
            .Where(a => a.Vencimento.AddDays(DiasParaBaixa) <= hoje && clientes.Any(c => c.Id == a.ClienteId && PerfilDe(c) == Perfil.Calote))
            .GroupBy(a => a.ClienteId).ToList();
        foreach (var g in vencidos)
            db.LancamentosFiado.Add(new LancamentoFiado
            {
                ClienteId = g.Key, Tipo = TipoLancamentoFiado.Baixa, Valor = g.Sum(a => a.Aberto), UsuarioId = usuarioId,
                Observacao = $"Sem pagar há mais de {DiasParaBaixa} dias e sem resposta: baixa (calote).",
            });
        await db.SaveChangesAsync();
        return vencidos.Count;
    }

    // Para "refazer o histórico": apaga a caderneta GERADA (compras das vendas do histórico, pagamentos nos caixas do
    // histórico e as baixas dos clientes fictícios). Os clientes ficam.
    public Task<int> ApagarHistoricoAsync(CancellationToken ct) =>
        db.LancamentosFiado.Where(l =>
                (l.Venda != null && l.Venda.Origem == OrigemVenda.Historico)
                || (l.SessaoCaixa != null && l.SessaoCaixa.ObservacaoFechamento == GeradorHistorico.Marca)
                || (l.Tipo == TipoLancamentoFiado.Baixa && l.Cliente!.Observacao != null && l.Cliente.Observacao.StartsWith(Marca)))
            .ExecuteDeleteAsync(ct);
}
