using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Simulador;

// Estado do simulador, compartilhado (singleton) entre o "trabalhador" e o controller.
public class SimuladorEstado(IConfiguration config)
{
    public const string Email = "simulador@marketplace.local";
    public const int NumeroCaixa = 9;

    // Liga/desliga em tempo real (o botão do dashboard). Ao reiniciar, volta ao valor da configuração.
    public bool Ativo { get; set; } = config.GetValue("Simulador:Ativo", false);

    // 1.0 = movimento normal de um mercadinho. 2.0 = o dobro de clientes (bom para gerar dados rápido).
    public double Intensidade { get; set; } = config.GetValue("Simulador:Intensidade", 1.0);

    public DateTimeOffset? UltimaRodada { get; set; }
    public string? UltimoErro { get; set; }
    public DateOnly? UltimaRotinaDaManha { get; set; }
}

// "Trabalhador em segundo plano" (BackgroundService): roda junto com a API, a cada minuto,
// fazendo os clientes chegarem, comprarem e pagarem — sempre pelas MESMAS regras da tela do caixa.
public class SimuladorClientes(
    IServiceScopeFactory escopos, SimuladorEstado estado, ClimaService clima, ILogger<SimuladorClientes> log)
    : BackgroundService
{
    // Clientes por hora num dia comum (pico no almoço e na saída do trabalho).
    private static readonly Dictionary<int, double> ClientesPorHora = new()
    {
        [7] = 4, [8] = 7, [9] = 6, [10] = 6, [11] = 9, [12] = 11, [13] = 8,
        [14] = 5, [15] = 5, [16] = 6, [17] = 10, [18] = 14, [19] = 12, [20] = 7,
    };

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        log.LogInformation("Simulador de clientes iniciado (ativo: {Ativo}, intensidade: {Intensidade}).",
            estado.Ativo, estado.Intensidade);
        await Task.Delay(TimeSpan.FromSeconds(20), parar); // deixa a API terminar de ligar

        using var relogio = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            if (!estado.Ativo) continue;
            try
            {
                await RodadaAsync(parar);
                estado.UltimoErro = null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                estado.UltimoErro = e.Message;
                log.LogError(e, "Erro na rodada do simulador");
            }
        } while (await relogio.WaitForNextTickAsync(parar));
    }

    // Uma "rodada" = um minuto do mercado.
    private async Task RodadaAsync(CancellationToken ct)
    {
        var agora = Relogio.AgoraBrasilia;
        estado.UltimaRodada = agora;
        var (abre, fecha) = Horario(agora);
        var aberto = agora.Hour >= abre && agora.Hour < fecha;
        var usuarioId = await UsuarioIdAsync();

        using (var scope = escopos.CreateScope())
        {
            var caixa = scope.ServiceProvider.GetRequiredService<CaixaService>();
            var sessao = await caixa.ObterSessaoAbertaAsync(usuarioId);

            if (!aberto)
            {
                if (sessao is not null) await FecharCaixaAsync(caixa, usuarioId);
                return;
            }
            if (sessao is null)
            {
                await RotinaDaManhaAsync(usuarioId, ct);
                await caixa.AbrirAsync(usuarioId, SimuladorEstado.NumeroCaixa, 200m);
                log.LogInformation("Simulador abriu o Caixa {Numero}.", SimuladorEstado.NumeroCaixa);
            }
        }

        var tempo = await clima.ObterAsync(ct);
        var clientes = Poisson(ClientesNesteMinuto(agora, tempo));
        for (var i = 0; i < clientes; i++)
            await AtenderClienteAsync(usuarioId, tempo, agora, ct);
    }

    // Usado pelo botão "atender clientes agora" (testes e demonstrações).
    public async Task<int> AtenderAgoraAsync(int quantidade, CancellationToken ct)
    {
        var usuarioId = await UsuarioIdAsync();
        using (var scope = escopos.CreateScope())
        {
            var caixa = scope.ServiceProvider.GetRequiredService<CaixaService>();
            if (await caixa.ObterSessaoAbertaAsync(usuarioId) is null)
                await caixa.AbrirAsync(usuarioId, SimuladorEstado.NumeroCaixa, 200m);
        }
        var tempo = await clima.ObterAsync(ct);
        var vendidos = 0;
        for (var i = 0; i < quantidade; i++)
            if (await AtenderClienteAsync(usuarioId, tempo, Relogio.AgoraBrasilia, ct)) vendidos++;
        return vendidos;
    }

    private static (int Abre, int Fecha) Horario(DateTimeOffset agora) =>
        agora.DayOfWeek == DayOfWeek.Sunday ? (8, 13) : (7, 21);

    private double ClientesNesteMinuto(DateTimeOffset agora, ClimaAgora tempo)
    {
        var porHora = ClientesPorHora.GetValueOrDefault(agora.Hour, 3);
        porHora *= agora.DayOfWeek switch
        {
            DayOfWeek.Saturday => 1.35,
            DayOfWeek.Friday => 1.15,
            DayOfWeek.Sunday => 0.9,
            _ => 1.0,
        };
        porHora *= agora.Day <= 10 ? 1.15 : agora.Day >= 25 ? 0.9 : 1.0; // salário no começo do mês
        if (tempo.Chovendo) porHora *= 0.75;                               // chuva: menos gente na rua
        return porHora * estado.Intensidade / 60.0;
    }

    // ------------------------------------------------------------------ um cliente

    private async Task<bool> AtenderClienteAsync(string usuarioId, ClimaAgora tempo, DateTimeOffset agora, CancellationToken ct)
    {
        // Um "escopo" por cliente: cada venda com o seu próprio DbContext, limpinho.
        using var scope = escopos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vendas = scope.ServiceProvider.GetRequiredService<VendaService>();

        var hoje = Relogio.HojeBrasilia;
        var produtos = await db.Produtos.AsNoTracking().Where(p => p.Ativo && p.EstoqueAtual > 0).ToListAsync(ct);
        var vencido = await db.Lotes.AsNoTracking()
            .Where(l => l.QuantidadeAtual > 0 && l.DataValidade < hoje)
            .GroupBy(l => l.ProdutoId).Select(g => new { g.Key, Qtd = g.Sum(l => l.QuantidadeAtual) })
            .ToDictionaryAsync(x => x.Key, x => x.Qtd, ct);

        // Só o que está na prateleira e dentro da validade.
        var opcoes = produtos
            .Select(p => (Produto: p, Habito: PerfilConsumo.Para(p), Disponivel: p.EstoqueAtual - vencido.GetValueOrDefault(p.Id)))
            .Where(o => o.Disponivel > 0)
            .ToList();
        if (opcoes.Count == 0) return false;

        var itens = new List<ItemVendaRequest>();
        var total = 0m;
        foreach (var (produto, habito, disponivel) in SortearCesta(opcoes, tempo, agora))
        {
            var quantidade = Math.Min(Quantidade(produto, habito), disponivel);
            if (produto.Unidade is not (Unidades.Quilo or Unidades.Litro)) quantidade = Math.Floor(quantidade);
            if (quantidade <= 0) continue;
            itens.Add(new ItemVendaRequest(produto.Id, quantidade));
            total += Math.Round(quantidade * produto.PrecoVenda, 2, MidpointRounding.AwayFromZero);
        }
        if (itens.Count == 0) return false;

        try
        {
            await vendas.FinalizarAsync(usuarioId, podeDescontoLivre: false, new NovaVendaRequest(itens, 0, [Pagamento(total)]));
            return true;
        }
        catch (EstoqueException e)
        {
            // Ex.: outro caixa levou o último item segundos antes. O cliente "desiste" e vai embora.
            log.LogDebug("Cliente simulado não conseguiu comprar: {Motivo}", e.Message);
            return false;
        }
    }

    // Tamanho da cesta: muita gente leva 1 ou 2 coisas; alguns fazem "rancho".
    private static readonly int[] TamanhosDeCesta = [1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 5, 5, 6, 7, 8, 10];

    private static IEnumerable<(Produto, Habito, decimal)> SortearCesta(
        List<(Produto Produto, Habito Habito, decimal Disponivel)> opcoes, ClimaAgora tempo, DateTimeOffset agora)
    {
        var tamanho = Math.Min(TamanhosDeCesta[Random.Shared.Next(TamanhosDeCesta.Length)], opcoes.Count);
        var pesos = opcoes.Select(o => o.Habito.Popularidade * PerfilConsumo.Multiplicador(o.Habito, tempo, agora)).ToList();
        var restantes = Enumerable.Range(0, opcoes.Count).ToList();

        // Sorteio "com peso" e sem repetir: o pão francês sai muito mais que o shampoo.
        for (var n = 0; n < tamanho; n++)
        {
            var soma = restantes.Sum(i => pesos[i]);
            var alvo = Random.Shared.NextDouble() * soma;
            foreach (var i in restantes)
            {
                alvo -= pesos[i];
                if (alvo > 0) continue;
                yield return opcoes[i];
                restantes.Remove(i);
                break;
            }
        }
    }

    private static decimal Quantidade(Produto produto, Habito habito)
    {
        if (produto.Unidade is Unidades.Quilo or Unidades.Litro)
        {
            var (min, max) = habito.PesoMax > 0 ? (habito.PesoMin, habito.PesoMax) : (0.3m, 1.5m);
            // Peso de balança: 3 casas (gramas).
            return Math.Round(min + (decimal)Random.Shared.NextDouble() * (max - min), 3);
        }
        if (habito.Pacotes is { Length: > 0 } pacotes)
            return pacotes[Random.Shared.Next(pacotes.Length)];
        return Random.Shared.NextDouble() < 0.85 ? 1 : 2;
    }

    // Pix 40% · Débito 30% · Crédito 15% · Dinheiro 15% (às vezes com nota "redonda" e troco).
    private static PagamentoRequest Pagamento(decimal total)
    {
        var sorteio = Random.Shared.NextDouble();
        if (sorteio < 0.40) return new("Pix", total);
        if (sorteio < 0.70) return new("Debito", total);
        if (sorteio < 0.85) return new("Credito", total);

        decimal[] notas = [2, 5, 10, 20, 50, 100, 200];
        var nota = notas.FirstOrDefault(n => n >= total);
        var paga = nota > 0 && Random.Shared.NextDouble() < 0.7 ? nota : total; // 30% pagam o valor exato
        return new("Dinheiro", paga);
    }

    // ------------------------------------------------------------------ abertura e fechamento

    // Às 07:00: o repositor tira da prateleira o que venceu há mais de 1 dia (fica 1 dia visível,
    // para os alertas) e o fornecedor entrega o que está abaixo do mínimo.
    private async Task RotinaDaManhaAsync(string usuarioId, CancellationToken ct)
    {
        var hoje = Relogio.HojeBrasilia;
        if (estado.UltimaRotinaDaManha == hoje) return;

        using var scope = escopos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var estoque = scope.ServiceProvider.GetRequiredService<EstoqueService>();

        var vencidos = await db.Lotes.AsNoTracking()
            .Where(l => l.QuantidadeAtual > 0 && l.DataValidade < hoje.AddDays(-1) && l.Produto!.Ativo)
            .ToListAsync(ct);
        foreach (var lote in vencidos)
            await estoque.RegistrarPerdaAsync(lote.ProdutoId, lote.QuantidadeAtual, "Vencido", lote.Id,
                "Retirado da prateleira pelo repositor (simulador)", usuarioId);

        var produtos = await db.Produtos.AsNoTracking().Include(p => p.Categoria).Where(p => p.Ativo).ToListAsync(ct);
        var repostos = 0;
        foreach (var p in produtos.Where(p => p.EstoqueAtual <= p.EstoqueMinimo * 1.5m))
        {
            var fracionado = p.Unidade is Unidades.Quilo or Unidades.Litro;
            var quantidade = Math.Max(p.EstoqueMinimo * 3, fracionado ? 5 : 6) - p.EstoqueAtual;
            quantidade = fracionado ? Math.Ceiling(quantidade * 2) / 2 : Math.Ceiling(quantidade);
            if (quantidade <= 0) continue;

            DateOnly? validade = p.ControlaValidade ? hoje.AddDays(DiasDeValidade(p)) : null;
            await estoque.RegistrarEntradaAsync(p.Id, quantidade, null, validade,
                "Pedido automático do fornecedor (simulador)", usuarioId);
            repostos++;
        }

        estado.UltimaRotinaDaManha = hoje;
        log.LogInformation("Rotina da manhã: {Vencidos} lote(s) vencido(s) retirado(s), {Repostos} produto(s) reposto(s).",
            vencidos.Count, repostos);
    }

    private static int DiasDeValidade(Produto p)
    {
        var nome = PerfilConsumo.SemAcento(p.Nome);
        if (nome.Contains("pao frances")) return 1;
        return PerfilConsumo.SemAcento(p.Categoria?.Nome ?? "") switch
        {
            "padaria" => 5,
            "hortifruti" => 7,
            "acougue" => 4,
            "frios e laticinios" => nome.Contains("queijo") || nome.Contains("presunto") ? 10 : 20,
            "bebidas" => 120,
            "mercearia" => 180,
            _ => 90,
        };
    }

    private async Task FecharCaixaAsync(CaixaService caixa, string usuarioId)
    {
        var sessao = await caixa.ObterSessaoAbertaAsync(usuarioId);
        var esperado = (await caixa.ResumirAsync(sessao!.Id)).DinheiroEsperado;

        // 1 em cada 10 dias o "operador" erra um troco: diferença pequena, para os relatórios terem o que mostrar.
        var diferenca = Random.Shared.NextDouble() < 0.10 ? Math.Round((decimal)(Random.Shared.NextDouble() * 4 - 2), 2) : 0;
        var resumo = await caixa.FecharAsync(usuarioId, Math.Max(0, esperado + diferenca), "Fechamento automático (simulador)");
        log.LogInformation("Simulador fechou o caixa: {Vendas} vendas, {Total:C}, diferença {Diferenca:C}.",
            resumo.QuantidadeVendas, resumo.TotalVendido, resumo.Diferenca);
    }

    // ------------------------------------------------------------------ utilidades

    private string? _usuarioId;

    private async Task<string> UsuarioIdAsync()
    {
        if (_usuarioId is not null) return _usuarioId;
        using var scope = escopos.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        _usuarioId = (await usuarios.FindByEmailAsync(SimuladorEstado.Email))?.Id
            ?? throw new InvalidOperationException("Usuário do simulador não existe (SeedSimulador).");
        return _usuarioId;
    }

    // Quantos clientes chegam num minuto, se a média é "lambda" (distribuição de Poisson).
    // Ex.: média 0,2 por minuto → na maioria dos minutos ninguém; às vezes 1; raramente 2.
    private static int Poisson(double lambda)
    {
        var limite = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do
        {
            k++;
            p *= Random.Shared.NextDouble();
        } while (p > limite);
        return k - 1;
    }
}
