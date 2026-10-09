using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Marketplace.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Simulador;

// Estado do simulador, compartilhado (singleton) entre o "trabalhador" e o controller.
public class SimuladorEstado(IConfiguration config)
{
    public const string Email = "simulador@marketplace.local";
    public const int NumeroCaixa = 9;

    // Em qual empresa (mercado) o simulador trabalha. Padrão: 1 (o Marketplace de demonstração).
    public int EmpresaId { get; } = config.GetValue("Simulador:EmpresaId", 1);

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
        var aberto = ComportamentoCliente.AbertoNaHora(DateOnly.FromDateTime(agora.DateTime), agora.Hour);
        var usuarioId = await UsuarioIdAsync();

        using (var scope = escopos.CriarEscopo(estado.EmpresaId))
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
        var porMinuto = ComportamentoCliente.ClientesNaHora(DateOnly.FromDateTime(agora.DateTime), agora.Hour, tempo, estado.Intensidade) / 60;
        var clientes = ComportamentoCliente.Poisson(porMinuto);
        for (var i = 0; i < clientes; i++)
            await AtenderClienteAsync(usuarioId, tempo, agora, ct);
    }

    // Usado pelo botão "atender clientes agora" (testes e demonstrações).
    public async Task<int> AtenderAgoraAsync(int quantidade, CancellationToken ct)
    {
        var usuarioId = await UsuarioIdAsync();
        using (var scope = escopos.CriarEscopo(estado.EmpresaId))
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

    // ------------------------------------------------------------------ um cliente

    private async Task<bool> AtenderClienteAsync(string usuarioId, ClimaAgora tempo, DateTimeOffset agora, CancellationToken ct)
    {
        // Um "escopo" por cliente: cada venda com o seu próprio DbContext, limpinho.
        using var scope = escopos.CriarEscopo(estado.EmpresaId);
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

        var itens = ComportamentoCliente.MontarCesta(opcoes, tempo, agora);
        var total = itens.Sum(i => Math.Round(i.Quantidade * opcoes.First(o => o.Produto.Id == i.ProdutoId).Produto.PrecoVenda, 2, MidpointRounding.AwayFromZero));
        if (itens.Count == 0) return false;

        try
        {
            await vendas.FinalizarAsync(usuarioId, podeDescontoLivre: false, new NovaVendaRequest(itens, 0, [ComportamentoCliente.Pagamento(total)]), OrigemVenda.Simulador);
            return true;
        }
        catch (EstoqueException e)
        {
            // Ex.: outro caixa levou o último item segundos antes. O cliente "desiste" e vai embora.
            log.LogDebug("Cliente simulado não conseguiu comprar: {Motivo}", e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ abertura e fechamento

    // Na abertura (8h), a rotina do "gerente automático":
    //  1. recebe os pedidos AUTOMÁTICOS que chegaram hoje (confere tudo e informa a validade dos perecíveis);
    //  2. o repositor tira da prateleira o que venceu há mais de 1 dia (fica 1 dia visível, para os alertas);
    //  3. faz os pedidos novos pela SUGESTÃO DE COMPRA e envia aos fornecedores (chegam em 1 a 4 dias).
    // Nada de estoque "mágico": se o pedido demora, o produto pode faltar — como num mercado de verdade.
    private async Task RotinaDaManhaAsync(string usuarioId, CancellationToken ct)
    {
        var hoje = Relogio.HojeBrasilia;
        if (estado.UltimaRotinaDaManha == hoje) return;

        using var scope = escopos.CriarEscopo(estado.EmpresaId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var estoque = scope.ServiceProvider.GetRequiredService<EstoqueService>();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasService>();

        // 1. Entregas do dia
        var chegaram = await db.PedidosCompra.AsNoTracking().Include(p => p.Itens).ThenInclude(i => i.Produto).ThenInclude(p => p!.Categoria)
            .Where(p => p.Automatico && p.Status == StatusPedido.Enviado && p.PrevisaoEntrega <= hoje) // os pedidos feitos por PESSOAS, quem confere são elas
            .ToListAsync(ct);
        foreach (var pedido in chegaram)
        {
            var conferencia = pedido.Itens.Select(i => new ItemRecebimento(i.Id, i.Quantidade,
                i.Produto!.ControlaValidade ? hoje.AddDays(DiasDeValidade(i.Produto)) : null, null)).ToList();
            await compras.ReceberAsync(pedido.Id, conferencia, usuarioId);
        }

        // 2. Vencidos para fora
        var vencidos = await db.Lotes.AsNoTracking()
            .Where(l => l.QuantidadeAtual > 0 && l.DataValidade < hoje.AddDays(-1) && l.Produto!.Ativo)
            .ToListAsync(ct);
        foreach (var lote in vencidos)
            await estoque.RegistrarPerdaAsync(lote.ProdutoId, lote.QuantidadeAtual, "Vencido", lote.Id,
                "Retirado da prateleira pelo repositor (simulador)", usuarioId);

        // 3. Pedidos novos, pela sugestão
        var novos = 0;
        foreach (var sugestao in await compras.SugestaoAsync(ct))
        {
            var itens = sugestao.Itens.Select(i => new ItemPedidoRequest(i.ProdutoId, i.Sugerido, null)).ToList();
            await compras.CriarAsync(new NovoPedidoRequest(sugestao.FornecedorId, itens, "Pedido automático (simulador)", Enviar: true),
                usuarioId, automatico: true);
            novos++;
        }

        estado.UltimaRotinaDaManha = hoje;
        log.LogInformation("Rotina da manhã: {Recebidos} pedido(s) recebido(s), {Vencidos} lote(s) vencido(s) retirado(s), {Novos} pedido(s) novo(s).",
            chegaram.Count, vencidos.Count, novos);
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
        using var scope = escopos.CriarEscopo(estado.EmpresaId);
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        _usuarioId = (await usuarios.FindByEmailAsync(SimuladorEstado.Email))?.Id
            ?? throw new InvalidOperationException("Usuário do simulador não existe (SeedSimulador).");
        return _usuarioId;
    }
}
