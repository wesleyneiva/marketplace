using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Marketplace.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Services.Simulador;

// Estado do simulador, compartilhado (singleton) entre o "trabalhador" e o controller.
// O bot trabalha em TODAS as empresas com "Vendas automáticas" ligadas (Empresa.VendasAutomaticas), cada uma com o seu
// robô de caixa, o seu horário e o seu clima. Simulador:Ativo é a chave geral (desligada = nenhuma empresa).
public class SimuladorEstado(IConfiguration config)
{
    // O robô da demonstração (empresa 1); o de cada empresa é caixa-automatico-<id>@sistema.local.
    public const string Email = "caixa-automatico-1@sistema.local";
    // Caixa do robô. Na demonstração (modo Completo), o Caixa 9, fora do plano. Nas lojas em teste (Só vendas), o ÚLTIMO
    // caixa do plano (num mercadinho de 1 caixa, o Caixa 1): o robô faz o papel do operador. Se uma pessoa estiver usando
    // esse caixa, o robô espera; pausar o robô fecha o caixa dele para a pessoa poder abrir.
    public const int NumeroCaixa = 9;
    public static int CaixaDoRobo(bool completo, int limiteCaixas) => completo ? NumeroCaixa : Math.Max(1, limiteCaixas);

    public static string EmailDoRobo(int empresaId) =>
        empresaId == SeedDemonstracao.EmpresaId ? Email : $"caixa-automatico-{empresaId}@sistema.local";

    public static bool EhRobo(string? email) =>
        email == Email || (email?.StartsWith("caixa-automatico-") == true && email.EndsWith("@sistema.local"));

    // Empresa da demonstração (gerar-historico e o financeiro de demonstração). Padrão: 1.
    public int EmpresaId { get; } = config.GetValue("Simulador:EmpresaId", 1);

    // Chave geral. Ao reiniciar, volta ao valor da configuração.
    public bool Ativo { get; set; } = config.GetValue("Simulador:Ativo", false);

    // 1.0 = movimento normal de um mercadinho. 2.0 = o dobro de clientes (bom para gerar dados rápido).
    public double Intensidade { get; set; } = config.GetValue("Simulador:Intensidade", 1.0);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Loja> _lojas = new();
    public Loja DaEmpresa(int empresaId) => _lojas.GetOrAdd(empresaId, _ => new Loja());

    // O que o bot sabe de cada loja (em memória: ao reiniciar, "pausado" volta a falso).
    public class Loja
    {
        public bool Pausado { get; set; }
        public DateTimeOffset? UltimaRodada { get; set; }
        public string? UltimoErro { get; set; }
        public DateOnly? UltimaRotinaDaManha { get; set; }
        public string? UsuarioId { get; set; }
    }
}

// "Trabalhador em segundo plano" (BackgroundService): roda junto com a API, a cada minuto,
// fazendo os clientes chegarem, comprarem e pagarem — sempre pelas MESMAS regras da tela do caixa.
public class SimuladorClientes(
    IServiceScopeFactory escopos, SimuladorEstado estado, ClimaService clima, LocaisDasLojas locais, ILogger<SimuladorClientes> log)
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
            List<(int Id, string Modo, int Caixas)> lojas;
            try
            {
                using var scope = escopos.CreateScope();
                lojas = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Empresas.AsNoTracking()
                    .Where(e => e.Ativa && e.VendasAutomaticas != ModosVendasAutomaticas.Desligado)
                    .Select(e => new { e.Id, e.VendasAutomaticas, e.LimiteCaixas }).ToListAsync(parar))
                    .Select(e => (e.Id, e.VendasAutomaticas, e.LimiteCaixas)).ToList();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Banco indisponível por um instante: tenta no próximo minuto (antes, isto derrubava a API inteira).
                log.LogWarning("Simulador: não consegui ler as lojas ({Erro}); tento de novo no próximo minuto.", e.Message);
                continue;
            }
            foreach (var (empresaId, modo, caixas) in lojas)
            {
                var loja = estado.DaEmpresa(empresaId);
                if (loja.Pausado) continue;
                try
                {
                    await RodadaAsync(empresaId, modo == ModosVendasAutomaticas.Completo, caixas, parar);
                    loja.UltimoErro = null;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    loja.UltimoErro = e.Message;
                    log.LogError(e, "Erro na rodada do simulador (empresa {Empresa})", empresaId);
                }
            }
        } while (await relogio.WaitForNextTickAsync(parar));
    }

    // Uma "rodada" = um minuto da loja. "completo" = a demonstração (compras, contas, fiado e depósitos também).
    private async Task RodadaAsync(int empresaId, bool completo, int limiteCaixas, CancellationToken ct)
    {
        locais.Aplicar(empresaId); // relógio e horário de funcionamento desta loja
        var loja = estado.DaEmpresa(empresaId);
        var agora = Relogio.Agora;
        loja.UltimaRodada = agora;
        var aberto = Expediente.AbertoAgora(agora);
        var usuarioId = await UsuarioIdAsync(empresaId);

        using (var scope = escopos.CriarEscopo(empresaId))
        {
            var caixa = scope.ServiceProvider.GetRequiredService<CaixaService>();
            var sessao = await caixa.ObterSessaoAbertaAsync(usuarioId);

            if (!aberto)
            {
                if (sessao is not null)
                {
                    await FecharCaixaAsync(caixa, usuarioId, empresaId);
                    if (completo)
                    {
                        // Fim do dia: o dinheiro da gaveta vai para o banco (fica o troco de amanhã).
                        var financeiro = scope.ServiceProvider.GetRequiredService<FinanceiroDemonstracao>();
                        if (await financeiro.DepositarAsync(sessao))
                            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
                    }
                }
                return;
            }
            if (sessao is null)
            {
                if (completo) await RotinaDaManhaAsync(empresaId, usuarioId, ct);
                var numero = SimuladorEstado.CaixaDoRobo(completo, limiteCaixas);
                SessaoCaixa aberta;
                try
                {
                    aberta = await caixa.AbrirAsync(usuarioId, numero, 200m, foraDoPlano: completo);
                }
                catch (CaixaException e)
                {
                    // Ex.: uma pessoa está usando o caixa agora. O robô tenta de novo no próximo minuto.
                    log.LogDebug("Simulador não abriu o Caixa {Numero} da empresa {Empresa}: {Motivo}", numero, empresaId, e.Message);
                    return;
                }
                if (completo)
                {
                    // Caderneta: quem chegou no "dia de pagar" vem acertar o fiado; calote antigo vira baixa.
                    var fiadoDemo = scope.ServiceProvider.GetRequiredService<FiadoDemonstracao>();
                    await fiadoDemo.GarantirClientesAsync(usuarioId);
                    var pagaram = await fiadoDemo.ReceberDoDiaAsync(aberta, Relogio.Hoje, usuarioId);
                    var baixas = await fiadoDemo.BaixarCalotesAsync(Relogio.Hoje, usuarioId);
                    log.LogInformation("Simulador abriu o Caixa {Numero} ({Pagaram} cliente(s) pagaram o fiado, {Baixas} baixa(s)).",
                        numero, pagaram, baixas);
                }
                else
                    log.LogInformation("Simulador abriu o Caixa {Numero} da empresa {Empresa}.", numero, empresaId);
            }
        }

        var tempo = await ClimaDaLojaAsync(empresaId, ct);
        var hoje = DateOnly.FromDateTime(agora.DateTime);
        var esporte = await EsporteAsync(empresaId, ct);
        if (esporte)
        {
            // A oficina anda: começa, termina e entrega as raquetes que o bot recebeu.
            using var oficina = escopos.CriarEscopo(empresaId);
            await OficinaDemonstracao.AndarComAsOrdensAsync(oficina.ServiceProvider.GetRequiredService<AppDbContext>());
        }
        var porMinuto = (esporte
            ? PerfilEsporte.ClientesNaHora(hoje, agora.Hour, tempo, estado.Intensidade)
            : ComportamentoCliente.ClientesNaHora(hoje, agora.Hour, tempo, estado.Intensidade)) / 60;
        var clientes = ComportamentoCliente.Poisson(porMinuto);
        for (var i = 0; i < clientes; i++)
            await AtenderClienteAsync(empresaId, completo, usuarioId, tempo, agora, ct);
    }

    // Pausar: o robô fecha o caixa dele agora (fechamento normal, vai para o Financeiro), para uma pessoa poder usar.
    public async Task<bool> FecharCaixaDoRoboAsync(int empresaId)
    {
        locais.Aplicar(empresaId);
        var usuarioId = await UsuarioIdAsync(empresaId);
        using var scope = escopos.CriarEscopo(empresaId);
        var caixa = scope.ServiceProvider.GetRequiredService<CaixaService>();
        if (await caixa.ObterSessaoAbertaAsync(usuarioId) is null) return false;
        await FecharCaixaAsync(caixa, usuarioId, empresaId);
        return true;
    }

    // Loja de esporte (Empresa.Ramo)? Muda o movimento, a cesta e o pagamento (PerfilEsporte).
    private async Task<bool> EsporteAsync(int empresaId, CancellationToken ct)
    {
        using var scope = escopos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Empresas.AsNoTracking().AnyAsync(e => e.Id == empresaId && e.Ramo == Ramos.Esporte, ct);
    }

    // O clima da cidade da loja (sem cidade cadastrada: um dia "neutro").
    private async Task<ClimaAgora> ClimaDaLojaAsync(int empresaId, CancellationToken ct) =>
        locais.Local(empresaId) is { } local ? await clima.ObterAsync(local, ct) : ClimaAgora.Neutro;

    // Usado pelo botão "atender clientes agora" (testes e demonstrações), na empresa de quem clicou.
    public async Task<int> AtenderAgoraAsync(int empresaId, bool completo, int limiteCaixas, int quantidade, CancellationToken ct)
    {
        locais.Aplicar(empresaId);
        var usuarioId = await UsuarioIdAsync(empresaId);
        using (var scope = escopos.CriarEscopo(empresaId))
        {
            var caixa = scope.ServiceProvider.GetRequiredService<CaixaService>();
            if (await caixa.ObterSessaoAbertaAsync(usuarioId) is null)
                await caixa.AbrirAsync(usuarioId, SimuladorEstado.CaixaDoRobo(completo, limiteCaixas), 200m, foraDoPlano: completo);
        }
        var tempo = await ClimaDaLojaAsync(empresaId, ct);
        var vendidos = 0;
        for (var i = 0; i < quantidade; i++)
            if (await AtenderClienteAsync(empresaId, completo, usuarioId, tempo, Relogio.Agora, ct)) vendidos++;
        return vendidos;
    }

    // ------------------------------------------------------------------ um cliente

    private async Task<bool> AtenderClienteAsync(int empresaId, bool completo, string usuarioId, ClimaAgora tempo, DateTimeOffset agora, CancellationToken ct)
    {
        // Um "escopo" por cliente: cada venda com o seu próprio DbContext, limpinho.
        using var scope = escopos.CriarEscopo(empresaId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vendas = scope.ServiceProvider.GetRequiredService<VendaService>();

        var hoje = Relogio.Hoje;
        var esporte = await EsporteAsync(empresaId, ct);
        // Serviço (encordoamento) não tem estoque: está sempre "disponível".
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.Modelo)
            .Where(p => p.Ativo && (p.EstoqueAtual > 0 || p.Servico)).ToListAsync(ct);
        var vencido = await db.Lotes.AsNoTracking()
            .Where(l => l.QuantidadeAtual > 0 && l.DataValidade < hoje)
            .GroupBy(l => l.ProdutoId).Select(g => new { g.Key, Qtd = g.Sum(l => l.QuantidadeAtual) })
            .ToDictionaryAsync(x => x.Key, x => x.Qtd, ct);

        // Só o que está na prateleira e dentro da validade.
        var disponiveis = produtos
            .Select(p => (Produto: p, Disponivel: p.Servico ? 99m : p.EstoqueAtual - vencido.GetValueOrDefault(p.Id)))
            .Where(o => o.Disponivel > 0);
        var opcoes = esporte
            ? PerfilEsporte.Opcoes(disponiveis)
            : disponiveis.Select(o => (o.Produto, Habito: PerfilConsumo.Para(o.Produto), o.Disponivel)).ToList();
        if (opcoes.Count == 0) return false;

        var itens = ComportamentoCliente.MontarCesta(opcoes, tempo, agora, esporte ? PerfilEsporte.TamanhosDeCesta : null);
        if (esporte) PerfilEsporte.CompletarCesta(itens, opcoes);
        var total = itens.Sum(i => Math.Round(i.Quantidade * opcoes.First(o => o.Produto.Id == i.ProdutoId).Produto.PrecoVenda, 2, MidpointRounding.AwayFromZero));
        if (itens.Count == 0) return false;

        try
        {
            // Na demonstração, ~3% levam no fiado (um cliente da caderneta que pode comprar); o resto paga como sempre.
            var noFiado = completo ? await scope.ServiceProvider.GetRequiredService<FiadoDemonstracao>().EscolherClienteAsync(total) : null;
            var pedido = noFiado is int clienteId
                ? new NovaVendaRequest(itens, 0, [new PagamentoRequest("Fiado", total)], clienteId)
                : new NovaVendaRequest(itens, 0, [esporte ? PerfilEsporte.Pagamento(total) : ComportamentoCliente.Pagamento(total)]);
            var venda = await vendas.FinalizarAsync(usuarioId, podeDescontoLivre: false, pedido, OrigemVenda.Simulador);
            // Loja de tênis: o encordoamento vendido vira ordem de serviço (o bot faz e entrega depois).
            if (esporte) await OficinaDemonstracao.AbrirOrdensAsync(db, venda, itens, produtos, usuarioId);
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

    // Na abertura, a rotina do "gerente automático":
    //  1. recebe os pedidos AUTOMÁTICOS que chegaram hoje (confere tudo e informa a validade dos perecíveis);
    //  2. o repositor tira da prateleira o que venceu há mais de 1 dia (fica 1 dia visível, para os alertas);
    //  3. faz os pedidos novos pela SUGESTÃO DE COMPRA e envia aos fornecedores (chegam em 1 a 4 dias).
    // Nada de estoque "mágico": se o pedido demora, o produto pode faltar — como num mercado de verdade.
    private async Task RotinaDaManhaAsync(int empresaId, string usuarioId, CancellationToken ct)
    {
        var hoje = Relogio.Hoje;
        var loja = estado.DaEmpresa(empresaId);
        if (loja.UltimaRotinaDaManha == hoje) return;

        using var scope = escopos.CriarEscopo(empresaId);
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

        // 4. Escritório: boletos dos pedidos recebidos, contas fixas dos próximos 35 dias e pagamento do que venceu.
        var financeiro = scope.ServiceProvider.GetRequiredService<FinanceiroDemonstracao>();
        var boletos = await financeiro.BoletosDosPedidosAsync(hoje.AddDays(-7), usuarioId);
        var fixas = await financeiro.ContasFixasAsync(hoje, hoje.AddDays(35), usuarioId);
        var pagas = await financeiro.PagarVencidasAsync(hoje, usuarioId);

        loja.UltimaRotinaDaManha = hoje;
        log.LogInformation("Rotina da manhã: {Recebidos} pedido(s) recebido(s), {Vencidos} lote(s) vencido(s) retirado(s), {Novos} pedido(s) novo(s); "
            + "{Boletos} boleto(s), {Fixas} conta(s) fixa(s) nova(s), {Pagas} conta(s) paga(s).",
            chegaram.Count, vencidos.Count, novos, boletos, fixas, pagas);
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

    private async Task FecharCaixaAsync(CaixaService caixa, string usuarioId, int empresaId)
    {
        var sessao = await caixa.ObterSessaoAbertaAsync(usuarioId);
        var esperado = (await caixa.ResumirAsync(sessao!.Id)).DinheiroEsperado;

        // 1 em cada 10 dias o "operador" erra um troco: diferença pequena, para os relatórios terem o que mostrar.
        var diferenca = Random.Shared.NextDouble() < 0.10 ? Math.Round((decimal)(Random.Shared.NextDouble() * 4 - 2), 2) : 0;
        var resumo = await caixa.FecharAsync(usuarioId, Math.Max(0, esperado + diferenca), "Fechamento automático (simulador)");
        log.LogInformation("Simulador fechou o caixa da empresa {Empresa}: {Vendas} vendas, {Total:C}, diferença {Diferenca:C}.",
            empresaId, resumo.QuantidadeVendas, resumo.TotalVendido, resumo.Diferenca);
    }

    // ------------------------------------------------------------------ utilidades

    // O robô de caixa da empresa (criado na primeira vez: perfil Caixa, sem senha, bloqueado para login).
    private async Task<string> UsuarioIdAsync(int empresaId)
    {
        var loja = estado.DaEmpresa(empresaId);
        return loja.UsuarioId ??= await SeedSimulador.GarantirRoboAsync(escopos, empresaId);
    }
}
