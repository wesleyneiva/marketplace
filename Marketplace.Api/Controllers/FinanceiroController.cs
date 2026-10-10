using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Financeiro: contas (onde o dinheiro está), plano de contas (por quê) e o livro (cada entrada e saída).
[ApiController]
[Route("api/financeiro")]
[Authorize(Roles = $"{Perfis.Administrador},{Perfis.Gerente}")]
public class FinanceiroController(AppDbContext db) : ControllerBase
{
    private const int LimiteExtrato = 2000;
    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ================================================================ CONTAS FINANCEIRAS

    // GET /api/financeiro/contas?incluirInativas=false → com o saldo de hoje
    [HttpGet("contas")]
    public async Task<List<ContaFinanceiraResponse>> Contas(bool incluirInativas = false)
    {
        var saldos = await Financeiro.SaldosAsync(db);
        var contas = await db.ContasFinanceiras.AsNoTracking()
            .Where(c => incluirInativas || c.Ativa)
            .Select(c => new { Conta = c, Movimentos = db.MovimentosFinanceiros.Count(m => m.ContaFinanceiraId == c.Id) })
            .ToListAsync();
        // Ordem natural: dinheiro (gaveta, cofre) → banco → maquininha.
        return contas.OrderBy(x => x.Conta.Tipo).ThenBy(x => x.Conta.Id).Select(x => Resposta(x.Conta, saldos.GetValueOrDefault(x.Conta.Id), x.Movimentos)).ToList();
    }

    [HttpPost("contas")]
    public async Task<ActionResult<ContaFinanceiraResponse>> CriarConta(ContaFinanceiraRequest r)
    {
        if (await NomeContaEmUsoAsync(r.Nome, null)) return Conflict(new { mensagem = "Já existe uma conta com este nome." });
        var conta = new ContaFinanceira();
        Preencher(conta, r);
        db.ContasFinanceiras.Add(conta);
        await db.SaveChangesAsync();
        return Resposta(conta, conta.SaldoInicial, 0);
    }

    // PUT /api/financeiro/contas/5 → nome, dados do banco e saldo inicial (implantação)
    [HttpPut("contas/{id:int}")]
    public async Task<ActionResult<ContaFinanceiraResponse>> EditarConta(int id, ContaFinanceiraRequest r)
    {
        var conta = await db.ContasFinanceiras.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (await NomeContaEmUsoAsync(r.Nome, id)) return Conflict(new { mensagem = "Já existe uma conta com este nome." });
        if (conta.Chave is not null && r.Tipo != conta.Tipo)
            return BadRequest(new { mensagem = "Esta conta é usada pelo sistema: o tipo não pode mudar." });
        Preencher(conta, r);
        await db.SaveChangesAsync();
        var saldo = (await Financeiro.SaldosAsync(db)).GetValueOrDefault(id);
        return Resposta(conta, saldo, await db.MovimentosFinanceiros.CountAsync(m => m.ContaFinanceiraId == id));
    }

    // POST /api/financeiro/contas/5/ativa?valor=false → desativar/reativar (só com saldo zerado)
    [HttpPost("contas/{id:int}/ativa")]
    public async Task<IActionResult> AtivarConta(int id, bool valor)
    {
        var conta = await db.ContasFinanceiras.FirstOrDefaultAsync(c => c.Id == id);
        if (conta is null) return NotFound();
        if (!valor)
        {
            if (conta.Chave is not null) return BadRequest(new { mensagem = "Esta conta é usada pelo sistema e não pode ser desativada (pode renomear)." });
            var saldo = (await Financeiro.SaldosAsync(db)).GetValueOrDefault(id);
            if (saldo != 0) return BadRequest(new { mensagem = $"A conta ainda tem saldo de {saldo.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}. Transfira o saldo para outra conta antes de desativar." });
        }
        conta.Ativa = valor;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ PLANO DE CONTAS

    [HttpGet("grupos")]
    public List<GrupoFinanceiroResponse> Grupos() =>
        Financeiro.Grupos.Select(g => new GrupoFinanceiroResponse(g.Grupo, (int)g.Grupo, g.Nome, g.Natureza, g.Explicacao)).ToList();

    // GET /api/financeiro/categorias?incluirInativas=false
    [HttpGet("categorias")]
    public async Task<List<CategoriaFinanceiraResponse>> Categorias(bool incluirInativas = false)
    {
        var lista = await db.CategoriasFinanceiras.AsNoTracking()
            .Where(c => incluirInativas || c.Ativa)
            .Select(c => new
            {
                Categoria = c,
                Usos = db.MovimentosFinanceiros.Count(m => m.CategoriaFinanceiraId == c.Id)
                       + db.ContasPagar.Count(p => p.CategoriaFinanceiraId == c.Id && p.PagaEm == null),
            }).ToListAsync();
        return lista.OrderBy(x => x.Categoria.Grupo).ThenBy(x => CodigoOrdenavel(x.Categoria.Codigo))
            .Select(x => Resposta(x.Categoria, x.Usos)).ToList();
    }

    [HttpPost("categorias")]
    public async Task<ActionResult<CategoriaFinanceiraResponse>> CriarCategoria(CategoriaFinanceiraRequest r)
    {
        if (!Enum.IsDefined(r.Grupo)) return BadRequest(new { mensagem = "Grupo inválido." });
        var natureza = Financeiro.NaturezaDoGrupo(r.Grupo) ?? r.Natureza;
        if (natureza is null) return BadRequest(new { mensagem = "Neste grupo, diga se a categoria é de entrada ou de saída." });
        if (r.Grupo == GrupoFinanceiro.ReceitaVendas)
            return BadRequest(new { mensagem = "As receitas de vendas vêm do PDV: use \"Outras receitas\" para entradas avulsas." });
        if (await NomeCategoriaEmUsoAsync(r.Nome, null)) return Conflict(new { mensagem = "Já existe uma categoria com este nome." });

        var existentes = await db.CategoriasFinanceiras.AsNoTracking().Where(c => c.Grupo == r.Grupo).ToListAsync();
        var categoria = new CategoriaFinanceira
        {
            Nome = r.Nome.Trim(), Grupo = r.Grupo, Natureza = natureza.Value, Codigo = SeedFinanceiro.ProximoCodigo(existentes, r.Grupo),
        };
        db.CategoriasFinanceiras.Add(categoria);
        await db.SaveChangesAsync();
        return Resposta(categoria, 0);
    }

    // PUT /api/financeiro/categorias/5 → renomear; trocar de grupo só se nunca foi usada (e não for do sistema)
    [HttpPut("categorias/{id:int}")]
    public async Task<ActionResult<CategoriaFinanceiraResponse>> EditarCategoria(int id, CategoriaFinanceiraRequest r)
    {
        var categoria = await db.CategoriasFinanceiras.FirstOrDefaultAsync(c => c.Id == id);
        if (categoria is null) return NotFound();
        if (await NomeCategoriaEmUsoAsync(r.Nome, id)) return Conflict(new { mensagem = "Já existe uma categoria com este nome." });
        var usos = await UsosAsync(id);

        var natureza = Financeiro.NaturezaDoGrupo(r.Grupo) ?? r.Natureza ?? categoria.Natureza;
        if (r.Grupo != categoria.Grupo || natureza != categoria.Natureza)
        {
            if (!Enum.IsDefined(r.Grupo) || r.Grupo == GrupoFinanceiro.ReceitaVendas) return BadRequest(new { mensagem = "Grupo inválido." });
            if (categoria.Chave is not null) return BadRequest(new { mensagem = "Categoria do sistema: só o nome pode mudar." });
            if (usos > 0) return BadRequest(new { mensagem = "Esta categoria já tem lançamentos: crie uma nova no grupo certo e desative esta." });
            if (r.Grupo != categoria.Grupo)
                categoria.Codigo = SeedFinanceiro.ProximoCodigo(await db.CategoriasFinanceiras.AsNoTracking().Where(c => c.Grupo == r.Grupo).ToListAsync(), r.Grupo);
            categoria.Grupo = r.Grupo;
            categoria.Natureza = natureza;
        }
        categoria.Nome = r.Nome.Trim();
        await db.SaveChangesAsync();
        return Resposta(categoria, usos);
    }

    // POST /api/financeiro/categorias/5/ativa?valor=false → desativar não apaga os lançamentos antigos
    [HttpPost("categorias/{id:int}/ativa")]
    public async Task<IActionResult> AtivarCategoria(int id, bool valor)
    {
        var categoria = await db.CategoriasFinanceiras.FirstOrDefaultAsync(c => c.Id == id);
        if (categoria is null) return NotFound();
        if (!valor && categoria.Chave is not null)
            return BadRequest(new { mensagem = "Esta categoria é usada pelo sistema e não pode ser desativada (pode renomear)." });
        categoria.Ativa = valor;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // GET /api/financeiro/categorias/sugerir?descricao=conta de luz&natureza=Saida
    [HttpGet("categorias/sugerir")]
    public async Task<SugestaoCategoriaResponse> Sugerir(string? descricao, NaturezaFinanceira natureza = NaturezaFinanceira.Saida)
    {
        if (string.IsNullOrWhiteSpace(descricao)) return new(null, null);
        var categorias = await db.CategoriasFinanceiras.AsNoTracking().ToListAsync();
        var id = Financeiro.SugerirCategoria(descricao, categorias, natureza);
        return new(id, categorias.FirstOrDefault(c => c.Id == id)?.Nome);
    }

    // ================================================================ O LIVRO

    // GET /api/financeiro/movimentos?de=2026-10-01&ate=2026-10-31&contaId=3&categoriaId=&busca=&origem=
    // Com uma conta só: saldo antes do período e saldo linha a linha (o "extrato do banco").
    [HttpGet("movimentos")]
    public async Task<ExtratoResponse> Extrato(DateOnly? de, DateOnly? ate, int? contaId, int? categoriaId, string? busca,
        OrigemMovimentoFinanceiro? origem)
    {
        var hoje = Relogio.Hoje;
        var fim = ate ?? hoje;
        var inicio = de ?? new DateOnly(fim.Year, fim.Month, 1);
        if (inicio > fim) (inicio, fim) = (fim, inicio);

        var q = db.MovimentosFinanceiros.AsNoTracking().Where(m => m.Data >= inicio && m.Data <= fim);
        if (contaId is int c) q = q.Where(m => m.ContaFinanceiraId == c);
        if (categoriaId is int cat) q = q.Where(m => m.CategoriaFinanceiraId == cat);
        if (origem is { } o) q = q.Where(m => m.Origem == o);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = $"%{busca.Trim()}%";
            q = q.Where(m => EF.Functions.ILike(EF.Functions.Unaccent(m.Descricao), EF.Functions.Unaccent(termo))
                             || (m.Documento != null && EF.Functions.ILike(m.Documento, termo)));
        }

        // Totais do filtro inteiro. Sem conta escolhida, transferência não conta (o dinheiro só mudou de lugar).
        var paraTotais = contaId is null ? q.Where(m => m.Origem != OrigemMovimentoFinanceiro.Transferencia) : q;
        var entradas = await paraTotais.Where(m => m.Tipo == TipoMovimentoFinanceiro.Entrada).SumAsync(m => (decimal?)m.Valor) ?? 0;
        var saidas = await paraTotais.Where(m => m.Tipo == TipoMovimentoFinanceiro.Saida).SumAsync(m => (decimal?)m.Valor) ?? 0;

        var linhas = await Projetar(q.OrderBy(m => m.Data).ThenBy(m => m.Id).Take(LimiteExtrato + 1)).ToListAsync();
        var cortado = linhas.Count > LimiteExtrato;
        if (cortado) linhas.RemoveAt(linhas.Count - 1);

        decimal? saldoAnterior = null, saldoFinal = null;
        SaldoImplantacao? implantacao = null;
        // Saldo linha a linha só faz sentido com UMA conta e sem outros filtros (senão as linhas não somam o saldo).
        if (contaId is int id && categoriaId is null && origem is null && string.IsNullOrWhiteSpace(busca) && !cortado)
        {
            var conta = await db.ContasFinanceiras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (conta is not null)
            {
                saldoAnterior = (await Financeiro.SaldosAsync(db, inicio.AddDays(-1))).GetValueOrDefault(id);
                saldoFinal = (await Financeiro.SaldosAsync(db, fim)).GetValueOrDefault(id);
                // Saldo de implantação dentro do período: linha própria; antes do período: já está no saldo anterior.
                if (conta.SaldoInicialEm >= inicio && conta.SaldoInicialEm <= fim)
                    implantacao = new SaldoImplantacao(conta.SaldoInicialEm, conta.SaldoInicial);
                var saldo = saldoAnterior.Value;
                var inicialSomado = implantacao is null;
                for (var i = 0; i < linhas.Count; i++)
                {
                    var m = linhas[i];
                    if (m.Data < conta.SaldoInicialEm) continue; // histórico de antes da implantação: só consulta
                    if (!inicialSomado) { saldo += conta.SaldoInicial; inicialSomado = true; }
                    saldo += m.Tipo == TipoMovimentoFinanceiro.Entrada ? m.Valor : -m.Valor;
                    linhas[i] = m with { SaldoApos = saldo };
                }
            }
        }
        return new ExtratoResponse(inicio, fim, entradas, saidas, saldoAnterior, saldoFinal, cortado, linhas, implantacao);
    }

    // POST /api/financeiro/movimentos → receita ou despesa avulsa, já paga/recebida
    [HttpPost("movimentos")]
    public async Task<ActionResult<MovimentoFinanceiroResponse>> Lancar(MovimentoRequest r)
    {
        var erro = await ValidarAsync(r);
        if (erro is null && r.Tipo == TipoMovimentoFinanceiro.Saida) erro = await Financeiro.FaltaDinheiroAsync(db, r.ContaId, r.Valor);
        if (erro is not null) return BadRequest(new { mensagem = erro });
        var m = new MovimentoFinanceiro { Origem = OrigemMovimentoFinanceiro.Manual, CriadoPorId = UsuarioId };
        Preencher(m, r);
        db.MovimentosFinanceiros.Add(m);
        await db.SaveChangesAsync();
        return await UmAsync(m.Id);
    }

    // PUT /api/financeiro/movimentos/5 → só lançamento manual (os outros se corrigem na origem)
    [HttpPut("movimentos/{id:int}")]
    public async Task<ActionResult<MovimentoFinanceiroResponse>> Editar(int id, MovimentoRequest r)
    {
        var m = await db.MovimentosFinanceiros.FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return NotFound();
        if (m.Origem != OrigemMovimentoFinanceiro.Manual) return BadRequest(new { mensagem = MotivoNaoEditavel(m) });
        var erro = await ValidarAsync(r, m);
        if (erro is null)
        {
            // Na mesma conta, o lançamento antigo "sai da conta" antes de entrar o novo.
            var antigo = m.ContaFinanceiraId != r.ContaId ? 0 : m.Tipo == TipoMovimentoFinanceiro.Saida ? m.Valor : -m.Valor;
            var novo = r.Tipo == TipoMovimentoFinanceiro.Saida ? r.Valor : -r.Valor;
            if (novo > antigo) erro = await Financeiro.FaltaDinheiroAsync(db, r.ContaId, novo, antigo); // a conta vai perder dinheiro
            // Trocou de conta e o antigo era ENTRADA: a conta antiga perde esse dinheiro.
            if (erro is null && m.ContaFinanceiraId != r.ContaId && m.Tipo == TipoMovimentoFinanceiro.Entrada)
                erro = await Financeiro.FaltaDinheiroAsync(db, m.ContaFinanceiraId, m.Valor);
        }
        if (erro is not null) return BadRequest(new { mensagem = erro });
        Preencher(m, r);
        await db.SaveChangesAsync();
        return await UmAsync(id);
    }

    // DELETE /api/financeiro/movimentos/5 → manual; transferência apaga as duas pernas
    [HttpDelete("movimentos/{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var m = await db.MovimentosFinanceiros.FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return NotFound();
        if (m.Conciliado) return BadRequest(new { mensagem = "Lançamento já conferido com o extrato do banco: desfaça a conciliação antes." });
        switch (m.Origem)
        {
            case OrigemMovimentoFinanceiro.Manual:
                if (m.Tipo == TipoMovimentoFinanceiro.Entrada && await Financeiro.FaltaDinheiroAsync(db, m.ContaFinanceiraId, m.Valor) is { } falta)
                    return BadRequest(new { mensagem = falta });
                db.MovimentosFinanceiros.Remove(m);
                await db.SaveChangesAsync();
                break;
            case OrigemMovimentoFinanceiro.Transferencia:
                if (await db.MovimentosFinanceiros.AnyAsync(x => x.TransferenciaId == m.TransferenciaId && x.Conciliado))
                    return BadRequest(new { mensagem = "A outra ponta desta transferência já foi conferida com o extrato: desfaça a conciliação antes." });
                var recebeu = await db.MovimentosFinanceiros.AsNoTracking()
                    .FirstAsync(x => x.TransferenciaId == m.TransferenciaId && x.Tipo == TipoMovimentoFinanceiro.Entrada);
                if (await Financeiro.FaltaDinheiroAsync(db, recebeu.ContaFinanceiraId, recebeu.Valor) is { } faltaTr)
                    return BadRequest(new { mensagem = faltaTr });
                await db.MovimentosFinanceiros.Where(x => x.TransferenciaId == m.TransferenciaId).ExecuteDeleteAsync();
                break;
            default:
                return BadRequest(new { mensagem = MotivoNaoEditavel(m) });
        }
        return NoContent();
    }

    // POST /api/financeiro/transferencias → ex.: depositar o dinheiro do cofre no banco (saída de um, entrada no outro)
    [HttpPost("transferencias")]
    public async Task<ActionResult<List<MovimentoFinanceiroResponse>>> Transferir(TransferenciaRequest r)
    {
        if (r.DeContaId == r.ParaContaId) return BadRequest(new { mensagem = "Escolha duas contas diferentes." });
        if (r.Data > Relogio.Hoje) return BadRequest(new { mensagem = "A data não pode ser no futuro." });
        var contas = await db.ContasFinanceiras.Where(c => c.Id == r.DeContaId || c.Id == r.ParaContaId).ToListAsync();
        var de = contas.FirstOrDefault(c => c.Id == r.DeContaId);
        var para = contas.FirstOrDefault(c => c.Id == r.ParaContaId);
        if (de is null || para is null) return BadRequest(new { mensagem = "Conta não encontrada." });
        if (!de.Ativa || !para.Ativa) return BadRequest(new { mensagem = "Uma das contas está desativada." });
        foreach (var c in contas.Where(c => r.Data < c.SaldoInicialEm))
            return BadRequest(new { mensagem = $"A data é anterior ao saldo inicial de \"{c.Nome}\" ({c.SaldoInicialEm:dd/MM/yyyy})." });

        if (await Financeiro.FaltaDinheiroAsync(db, de.Id, r.Valor) is { } falta) return BadRequest(new { mensagem = falta });

        var guid = Guid.NewGuid();
        var descricao = string.IsNullOrWhiteSpace(r.Descricao) ? null : r.Descricao.Trim();
        MovimentoFinanceiro Perna(TipoMovimentoFinanceiro tipo, ContaFinanceira conta, ContaFinanceira outra) => new()
        {
            Data = r.Data, Tipo = tipo, Valor = r.Valor, ContaFinanceiraId = conta.Id,
            Descricao = descricao ?? (tipo == TipoMovimentoFinanceiro.Saida ? $"Transferência para {outra.Nome}" : $"Transferência de {outra.Nome}"),
            Origem = OrigemMovimentoFinanceiro.Transferencia, TransferenciaId = guid, CriadoPorId = UsuarioId,
        };
        var saida = Perna(TipoMovimentoFinanceiro.Saida, de, para);
        var entrada = Perna(TipoMovimentoFinanceiro.Entrada, para, de);
        db.MovimentosFinanceiros.AddRange(saida, entrada);
        await db.SaveChangesAsync();
        return await Projetar(db.MovimentosFinanceiros.AsNoTracking().Where(m => m.TransferenciaId == guid).OrderBy(m => m.Id)).ToListAsync();
    }

    // ================================================================ apoio

    private async Task<string?> ValidarAsync(MovimentoRequest r, MovimentoFinanceiro? atual = null)
    {
        if (!Enum.IsDefined(r.Tipo)) return "Tipo inválido.";
        if (r.Data > Relogio.Hoje) return "A data não pode ser no futuro: para algo a pagar depois, use Contas a pagar.";
        var conta = await db.ContasFinanceiras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ContaId);
        if (conta is null) return "Conta não encontrada.";
        if (!conta.Ativa && conta.Id != atual?.ContaFinanceiraId) return "Esta conta está desativada.";
        if (r.Data < conta.SaldoInicialEm) return $"A data é anterior ao saldo inicial desta conta ({conta.SaldoInicialEm:dd/MM/yyyy}).";
        var categoria = await db.CategoriasFinanceiras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CategoriaId);
        if (categoria is null) return "Escolha a categoria.";
        if (!categoria.Ativa && categoria.Id != atual?.CategoriaFinanceiraId) return "Esta categoria está desativada.";
        if (categoria.Grupo == GrupoFinanceiro.ReceitaVendas) return "Vendas entram sozinhas pelo PDV: use \"Outras receitas\".";
        var natureza = r.Tipo == TipoMovimentoFinanceiro.Entrada ? NaturezaFinanceira.Entrada : NaturezaFinanceira.Saida;
        if (categoria.Natureza != natureza)
            return natureza == NaturezaFinanceira.Entrada ? $"\"{categoria.Nome}\" é categoria de saída." : $"\"{categoria.Nome}\" é categoria de entrada.";
        return null;
    }

    private static void Preencher(MovimentoFinanceiro m, MovimentoRequest r)
    {
        m.Tipo = r.Tipo;
        m.Data = r.Data;
        m.Valor = r.Valor;
        m.ContaFinanceiraId = r.ContaId;
        m.CategoriaFinanceiraId = r.CategoriaId;
        m.Descricao = r.Descricao.Trim();
        m.Documento = string.IsNullOrWhiteSpace(r.Documento) ? null : r.Documento.Trim();
    }

    private static void Preencher(ContaFinanceira c, ContaFinanceiraRequest r)
    {
        static string? Limpo(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        c.Nome = r.Nome.Trim();
        c.Tipo = r.Tipo;
        c.Banco = Limpo(r.Banco);
        c.Agencia = Limpo(r.Agencia);
        c.Numero = Limpo(r.Numero);
        c.SaldoInicial = r.SaldoInicial;
        c.SaldoInicialEm = r.SaldoInicialEm;
    }

    private static string MotivoNaoEditavel(MovimentoFinanceiro m) => m.Origem switch
    {
        OrigemMovimentoFinanceiro.ContaPagar => "Este lançamento é o pagamento de uma conta: desfaça o pagamento em Contas a pagar.",
        OrigemMovimentoFinanceiro.Transferencia => "Transferência não se edita: exclua e lance de novo.",
        _ => "Este lançamento foi gerado pelo sistema e não pode ser alterado aqui.",
    };

    private async Task<ActionResult<MovimentoFinanceiroResponse>> UmAsync(int id) =>
        await Projetar(db.MovimentosFinanceiros.AsNoTracking().Where(m => m.Id == id)).FirstAsync();

    private static IQueryable<MovimentoFinanceiroResponse> Projetar(IQueryable<MovimentoFinanceiro> q) =>
        q.Select(m => new MovimentoFinanceiroResponse(m.Id, m.Data, m.Tipo, m.Valor, m.ContaFinanceiraId, m.ContaFinanceira!.Nome,
            m.CategoriaFinanceiraId, m.CategoriaFinanceira!.Nome, m.CategoriaFinanceira!.Codigo, m.Descricao, m.Documento,
            m.Origem, m.ContaPagarId, m.TransferenciaId, m.Conciliado, m.CriadoPor!.NomeCompleto, null));

    private static ContaFinanceiraResponse Resposta(ContaFinanceira c, decimal saldo, int movimentos) =>
        new(c.Id, c.Nome, c.Tipo, c.Banco, c.Agencia, c.Numero, c.SaldoInicial, c.SaldoInicialEm, saldo, movimentos, c.Chave is not null, c.Ativa);

    private static CategoriaFinanceiraResponse Resposta(CategoriaFinanceira c, int usos) =>
        new(c.Id, c.Codigo, c.Nome, c.Grupo, Financeiro.NomeGrupo(c.Grupo), c.Natureza, c.Chave is not null, c.Ativa, usos);

    // "6.10" depois de "6.09" (e não depois de "6.1").
    private static int CodigoOrdenavel(string codigo) => int.TryParse(codigo.Split('.').Last(), out var n) ? n : 0;

    private async Task<int> UsosAsync(int categoriaId) =>
        await db.MovimentosFinanceiros.CountAsync(m => m.CategoriaFinanceiraId == categoriaId)
        + await db.ContasPagar.CountAsync(p => p.CategoriaFinanceiraId == categoriaId);

    private Task<bool> NomeContaEmUsoAsync(string nome, int? id) =>
        db.ContasFinanceiras.AnyAsync(c => c.Nome.ToLower() == nome.Trim().ToLower() && c.Id != id);

    private Task<bool> NomeCategoriaEmUsoAsync(string nome, int? id) =>
        db.CategoriasFinanceiras.AnyAsync(c => c.Nome.ToLower() == nome.Trim().ToLower() && c.Id != id);
}
