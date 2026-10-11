using System.Security.Claims;
using System.Text.RegularExpressions;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Fiado (a caderneta): clientes da loja, o que devem, receber e dar baixa. A venda no fiado é feita pelo PDV (VendaService).
[ApiController]
[Route("api/fiado")]
[Authorize]
public partial class FiadoController(AppDbContext db, FiadoService fiado, CaixaService caixa) : ControllerBase
{
    private const string Gerencia = $"{Perfis.Administrador},{Perfis.Gerente}";
    private static readonly FormaPagamento[] FormasDeReceber = [FormaPagamento.Dinheiro, FormaPagamento.Pix, FormaPagamento.Debito, FormaPagamento.Credito];
    private string UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ================================================================ PAINEL E CADASTRO (gerência)

    // GET /api/fiado/resumo
    [HttpGet("resumo")]
    [Authorize(Roles = Gerencia)]
    public async Task<ResumoFiadoResponse> Resumo()
    {
        var situacoes = await fiado.SituacoesAsync();
        var hoje = Relogio.Hoje;
        var inicioMes = Relogio.InicioDoDiaUtc(new DateOnly(hoje.Year, hoje.Month, 1));
        var doMes = db.LancamentosFiado.AsNoTracking().Where(l => l.DataHora >= inicioMes);
        var vendido = await doMes.Where(l => l.Tipo == TipoLancamentoFiado.Compra).SumAsync(l => (decimal?)l.Valor) ?? 0;
        var estornado = await doMes.Where(l => l.Tipo == TipoLancamentoFiado.Estorno).SumAsync(l => (decimal?)l.Valor) ?? 0;
        var recebido = await doMes.Where(l => l.Tipo == TipoLancamentoFiado.Pagamento).SumAsync(l => (decimal?)l.Valor) ?? 0;
        var ativos = await db.Clientes.Where(c => c.Ativo).Select(c => new { c.LimiteCredito }).ToListAsync();
        var com = situacoes.Values.Where(s => s.Saldo > 0).ToList();
        return new ResumoFiadoResponse(
            com.Sum(s => s.Saldo), com.Sum(s => s.Vencido), com.Count, com.Count(s => s.DiasAtraso > 0),
            com.Count(s => s.DiasAtraso > FiadoService.DiasDeTolerancia),
            com.Where(s => s.ProximoVencimento is { } p && p <= hoje.AddDays(7)).Sum(s => s.ValorProximo),
            vendido - estornado, recebido, ativos.Count, ativos.Sum(c => c.LimiteCredito));
    }

    // GET /api/fiado/clientes?busca=&situacao=todos|devendo|atrasados|inativos
    [HttpGet("clientes")]
    [Authorize(Roles = Gerencia)]
    public async Task<List<ClienteFiadoResponse>> Clientes(string? busca, string situacao = "todos")
    {
        var q = db.Clientes.AsNoTracking();
        q = situacao == "inativos" ? q.Where(c => !c.Ativo) : q.Where(c => c.Ativo);
        q = Buscar(q, busca);
        var clientes = await q.OrderBy(c => c.Nome).Take(1000).ToListAsync();
        var situacoes = await fiado.SituacoesAsync(clientes.Select(c => c.Id).ToList());
        var lista = clientes.Select(c => Resposta(c, situacoes.GetValueOrDefault(c.Id)));
        lista = situacao switch
        {
            "devendo" => lista.Where(c => c.Saldo > 0).OrderByDescending(c => c.Saldo),
            "atrasados" => lista.Where(c => c.DiasAtraso > 0).OrderByDescending(c => c.DiasAtraso),
            _ => lista,
        };
        return lista.ToList();
    }

    // GET /api/fiado/clientes/5 → a caderneta (extrato com saldo linha a linha)
    [HttpGet("clientes/{id:int}")]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<CadernetaResponse>> Caderneta(int id)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null) return NotFound();
        var lancamentos = await db.LancamentosFiado.AsNoTracking().Where(l => l.ClienteId == id)
            .OrderBy(l => l.DataHora).ThenBy(l => l.Id)
            .Select(l => new { l.Id, l.DataHora, l.Tipo, l.Valor, l.Vencimento, l.VendaId, l.Forma, l.SessaoCaixaId, l.MovimentoFinanceiroId,
                l.Observacao, Usuario = l.Usuario!.NomeCompleto, Conciliado = l.MovimentoFinanceiro != null && l.MovimentoFinanceiro.Conciliado })
            .ToListAsync();
        decimal saldo = 0;
        var extrato = lancamentos.Select(l =>
        {
            saldo += l.Tipo == TipoLancamentoFiado.Compra ? l.Valor : -l.Valor;
            return new LancamentoFiadoResponse(l.Id, l.DataHora, l.Tipo, l.Valor, l.Vencimento, l.VendaId, l.Forma?.ToString(),
                l.SessaoCaixaId != null, l.Observacao, l.Usuario, saldo,
                PodeDesfazer: l.Tipo == TipoLancamentoFiado.Pagamento && l.MovimentoFinanceiroId != null && !l.Conciliado);
        }).Reverse().ToList(); // mais recente primeiro
        return new CadernetaResponse(Resposta(cliente, await fiado.SituacaoAsync(id)), extrato);
    }

    [HttpPost("clientes")]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<ClienteFiadoResponse>> Criar(ClienteRequest r)
    {
        var cliente = new Cliente { CriadoPorId = UsuarioId };
        if (await PreencherAsync(cliente, r) is { } erro) return BadRequest(new { mensagem = erro });
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return Resposta(cliente, null);
    }

    [HttpPut("clientes/{id:int}")]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<ClienteFiadoResponse>> Editar(int id, ClienteRequest r)
    {
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null) return NotFound();
        if (await PreencherAsync(cliente, r) is { } erro) return BadRequest(new { mensagem = erro });
        await db.SaveChangesAsync();
        return Resposta(cliente, await fiado.SituacaoAsync(id));
    }

    // POST /api/fiado/clientes/5/ativo?valor=false → desativar (só sem dívida) / reativar
    [HttpPost("clientes/{id:int}/ativo")]
    [Authorize(Roles = Gerencia)]
    public async Task<IActionResult> Ativo(int id, bool valor)
    {
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null) return NotFound();
        if (!valor && (await fiado.SituacaoAsync(id)).Saldo > 0)
            return BadRequest(new { mensagem = "O cliente ainda deve: receba ou dê baixa antes de desativar." });
        cliente.Ativo = valor;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/fiado/clientes/5/pagamentos → recebimento FORA do caixa (entra no Financeiro na conta escolhida)
    [HttpPost("clientes/{id:int}/pagamentos")]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<ClienteFiadoResponse>> Receber(int id, PagamentoFiadoRequest r)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null) return NotFound();
        if (!Enum.TryParse<FormaPagamento>(r.Forma, true, out var forma) || !FormasDeReceber.Contains(forma))
            return BadRequest(new { mensagem = "Forma: Dinheiro, Pix, Debito ou Credito." });
        if (r.Data > Relogio.Hoje) return BadRequest(new { mensagem = "A data não pode ser no futuro." });
        var situacao = await fiado.SituacaoAsync(id);
        if (r.Valor > situacao.Saldo) return BadRequest(new { mensagem = $"{cliente.Nome} deve {Real(situacao.Saldo)}: o recebimento não pode passar disso." });
        var conta = await db.ContasFinanceiras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ContaFinanceiraId && c.Ativa);
        if (conta is null) return BadRequest(new { mensagem = "Escolha em qual conta o dinheiro entrou." });
        if (r.Data < conta.SaldoInicialEm) return BadRequest(new { mensagem = $"A data é anterior ao saldo inicial de \"{conta.Nome}\"." });

        var movimento = new MovimentoFinanceiro
        {
            Data = r.Data, Tipo = TipoMovimentoFinanceiro.Entrada, Valor = r.Valor, ContaFinanceiraId = conta.Id,
            CategoriaFinanceiraId = await Financeiro.CategoriaAsync(db, ChavesFinanceiras.RecebimentoFiado),
            Descricao = Cortar($"Fiado recebido de {cliente.Nome} ({FinanceiroCaixa.NomeForma(forma)})", 200),
            Origem = OrigemMovimentoFinanceiro.Fiado, CriadoPorId = UsuarioId,
        };
        db.LancamentosFiado.Add(new LancamentoFiado
        {
            ClienteId = id, Tipo = TipoLancamentoFiado.Pagamento, Valor = r.Valor, Forma = forma, MovimentoFinanceiro = movimento,
            DataHora = r.Data == Relogio.Hoje ? DateTimeOffset.UtcNow : Relogio.InicioDoDiaUtc(r.Data).AddHours(12),
            Observacao = Limpo(r.Observacao), UsuarioId = UsuarioId,
        });
        await db.SaveChangesAsync();
        return Resposta(cliente, await fiado.SituacaoAsync(id));
    }

    // DELETE /api/fiado/pagamentos/9 → desfaz um recebimento lançado FORA do caixa (errado)
    [HttpDelete("pagamentos/{lancamentoId:long}")]
    [Authorize(Roles = Gerencia)]
    public async Task<IActionResult> DesfazerPagamento(long lancamentoId)
    {
        var l = await db.LancamentosFiado.Include(x => x.MovimentoFinanceiro).FirstOrDefaultAsync(x => x.Id == lancamentoId);
        if (l is null) return NotFound();
        if (l.Tipo != TipoLancamentoFiado.Pagamento || l.MovimentoFinanceiro is null)
            return BadRequest(new { mensagem = "Só dá para desfazer recebimento lançado aqui (o do caixa entrou no fechamento)." });
        if (l.MovimentoFinanceiro.Conciliado) return BadRequest(new { mensagem = "Já conferido com o extrato do banco: desfaça a conciliação antes." });
        var movimento = l.MovimentoFinanceiro;
        db.LancamentosFiado.Remove(l);
        db.MovimentosFinanceiros.Remove(movimento);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/fiado/clientes/5/baixa → perdão / calote (só Administrador): vira "Inadimplência" na DRE
    [HttpPost("clientes/{id:int}/baixa")]
    [Authorize(Roles = Perfis.Administrador)]
    public async Task<ActionResult<ClienteFiadoResponse>> Baixa(int id, BaixaFiadoRequest r)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null) return NotFound();
        var situacao = await fiado.SituacaoAsync(id);
        if (r.Valor > situacao.Saldo) return BadRequest(new { mensagem = $"{cliente.Nome} deve {Real(situacao.Saldo)}: a baixa não pode passar disso." });
        db.LancamentosFiado.Add(new LancamentoFiado
        {
            ClienteId = id, Tipo = TipoLancamentoFiado.Baixa, Valor = r.Valor, Observacao = Cortar(r.Motivo.Trim(), 200), UsuarioId = UsuarioId,
        });
        await db.SaveChangesAsync();
        return Resposta(cliente, await fiado.SituacaoAsync(id));
    }

    // ================================================================ NO CAIXA (todos os perfis)

    // GET /api/fiado/pdv/buscar?termo=maria → para o operador escolher o cliente
    [HttpGet("pdv/buscar")]
    public async Task<List<ClientePdvResponse>> BuscarNoPdv(string? termo)
    {
        if (string.IsNullOrWhiteSpace(termo) || termo.Trim().Length < 2) return [];
        var clientes = await Buscar(db.Clientes.AsNoTracking().Where(c => c.Ativo), termo).OrderBy(c => c.Nome).Take(8).ToListAsync();
        var situacoes = await fiado.SituacoesAsync(clientes.Select(c => c.Id).ToList());
        return clientes.Select(c =>
        {
            var s = situacoes.GetValueOrDefault(c.Id) ?? new SituacaoFiado(0, 0, 0, null, 0, null, null);
            var digitos = SoDigitos(c.Telefone);
            return new ClientePdvResponse(c.Id, c.Nome, c.Apelido, digitos.Length >= 4 ? digitos[^4..] : null, s.Saldo, c.LimiteCredito,
                Math.Max(0, c.LimiteCredito - s.Saldo), s.Vencido, s.DiasAtraso, c.Bloqueado, FiadoService.MotivoParaGerente(c, s, 0));
        }).ToList();
    }

    // POST /api/fiado/pdv/receber → o cliente veio pagar no balcão: entra no caixa aberto (e no fechamento)
    [HttpPost("pdv/receber")]
    public async Task<ActionResult<ReciboFiadoResponse>> ReceberNoCaixa(ReceberNoCaixaRequest r)
    {
        var sessao = await caixa.ObterSessaoAbertaAsync(UsuarioId);
        if (sessao is null) return BadRequest(new { mensagem = "Abra o caixa antes de receber." });
        if (!Enum.TryParse<FormaPagamento>(r.Forma, true, out var forma) || !FormasDeReceber.Contains(forma))
            return BadRequest(new { mensagem = "Forma: Dinheiro, Pix, Debito ou Credito." });
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ClienteId);
        if (cliente is null) return BadRequest(new { mensagem = "Cliente não encontrado." });
        var antes = await fiado.SituacaoAsync(cliente.Id);
        if (r.Valor > antes.Saldo) return BadRequest(new { mensagem = $"{cliente.Nome} deve {Real(antes.Saldo)}: receba até esse valor." });

        var lancamento = new LancamentoFiado
        {
            ClienteId = cliente.Id, Tipo = TipoLancamentoFiado.Pagamento, Valor = r.Valor, Forma = forma, SessaoCaixaId = sessao.Id,
            UsuarioId = UsuarioId, Observacao = $"Recebido no Caixa {sessao.NumeroCaixa}",
        };
        db.LancamentosFiado.Add(lancamento);
        await db.SaveChangesAsync();
        var operador = await db.Users.Where(u => u.Id == UsuarioId).Select(u => u.NomeCompleto).FirstOrDefaultAsync() ?? "";
        return new ReciboFiadoResponse(cliente.Nome, r.Valor, forma.ToString(), antes.Saldo, antes.Saldo - r.Valor, lancamento.DataHora, operador);
    }

    // ================================================================ apoio

    // Busca por nome/apelido (sem acento), telefone ou CPF (só os dígitos).
    private static IQueryable<Cliente> Buscar(IQueryable<Cliente> q, string? busca)
    {
        if (string.IsNullOrWhiteSpace(busca)) return q;
        var termo = busca.Trim();
        var digitos = SoDigitos(termo);
        var like = $"%{termo}%";
        return q.Where(c => EF.Functions.ILike(EF.Functions.Unaccent(c.Nome), EF.Functions.Unaccent(like))
                            || (c.Apelido != null && EF.Functions.ILike(EF.Functions.Unaccent(c.Apelido), EF.Functions.Unaccent(like)))
                            || (digitos.Length >= 3 && ((c.Telefone != null && c.Telefone.Contains(digitos)) || (c.Cpf != null && c.Cpf.Contains(digitos)))));
    }

    private async Task<string?> PreencherAsync(Cliente c, ClienteRequest r)
    {
        var cpf = SoDigitos(r.Cpf);
        if (cpf.Length > 0 && !CpfValido(cpf)) return "CPF inválido (confira os números ou deixe em branco).";
        if (cpf.Length > 0 && await db.Clientes.AnyAsync(x => x.Cpf == cpf && x.Id != c.Id)) return "Já existe um cliente com este CPF.";
        var telefone = SoDigitos(r.Telefone);
        if (telefone.Length is > 0 and (< 10 or > 13)) return "Telefone: com DDD (ex.: 51 99999-8888).";
        c.Nome = r.Nome.Trim();
        c.Apelido = Limpo(r.Apelido);
        c.Telefone = telefone.Length > 0 ? telefone : null;
        c.Cpf = cpf.Length > 0 ? cpf : null;
        c.Endereco = Limpo(r.Endereco);
        c.LimiteCredito = Math.Round(r.LimiteCredito, 2);
        c.DiaPagamento = r.DiaPagamento;
        c.Bloqueado = r.Bloqueado;
        c.Observacao = Limpo(r.Observacao);
        return null;
    }

    private static ClienteFiadoResponse Resposta(Cliente c, SituacaoFiado? s)
    {
        s ??= new SituacaoFiado(0, 0, 0, null, 0, null, null);
        return new ClienteFiadoResponse(c.Id, c.Nome, c.Apelido, c.Telefone, c.Cpf, c.Endereco, c.Observacao, c.LimiteCredito, c.DiaPagamento,
            c.Bloqueado, c.Ativo, s.Saldo, Math.Max(0, c.LimiteCredito - s.Saldo), s.Vencido, s.DiasAtraso, s.ProximoVencimento, s.ValorProximo,
            s.UltimaCompra, s.UltimoPagamento);
    }

    // CPF: 11 dígitos, não todos iguais, e os dois dígitos verificadores certos.
    public static bool CpfValido(string cpf)
    {
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1) return false;
        int Digito(int tamanho)
        {
            var soma = Enumerable.Range(0, tamanho).Sum(i => (cpf[i] - '0') * (tamanho + 1 - i));
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
        return Digito(9) == cpf[9] - '0' && Digito(10) == cpf[10] - '0';
    }

    [GeneratedRegex("[^0-9]")] private static partial Regex NaoDigito();
    private static string SoDigitos(string? t) => string.IsNullOrEmpty(t) ? "" : NaoDigito().Replace(t, "");
    private static string? Limpo(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
    private static string Cortar(string t, int n) => t.Length > n ? t[..n] : t;
    private static string Real(decimal v) => v.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
