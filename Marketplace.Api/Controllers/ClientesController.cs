using System.Security.Claims;
using Marketplace.Api.Contracts;
using Marketplace.Api.Data;
using Marketplace.Api.Models;
using Marketplace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

// Clientes da loja: cadastro e histórico (gerência) e identificação no caixa (todos). O fiado tem as telas dele.
[ApiController]
[Route("api/clientes")]
[Authorize]
public class ClientesController(AppDbContext db, ClientesService clientes) : ControllerBase
{
    private const string Gerencia = $"{Perfis.Administrador},{Perfis.Gerente}";
    private string UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET /api/clientes?busca=&filtro=todos|aniversariantes|sumidos|novos|inativos&pagina=1
    [HttpGet]
    [Authorize(Roles = Gerencia)]
    public Task<ClientesPagina> Listar(string? busca, string filtro = "todos", int pagina = 1, int tamanho = 25) =>
        clientes.ListarAsync(busca, filtro, Math.Max(pagina, 1), Math.Clamp(tamanho, 1, 100));

    [HttpGet("{id:int}")]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<ClienteLojaDetalheResponse>> Detalhe(int id) =>
        await clientes.DetalheAsync(id) is { } d ? d : NotFound();

    [HttpPost]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<ClienteResponse>> Criar(ClienteCadastroRequest r)
    {
        var cliente = new Cliente { CriadoPorId = UsuarioId };
        if (await clientes.PreencherAsync(cliente, r) is { } erro) return BadRequest(new { mensagem = erro });
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return ClientesService.Resposta(cliente);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Gerencia)]
    public async Task<ActionResult<ClienteResponse>> Editar(int id, ClienteCadastroRequest r)
    {
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null) return NotFound();
        if (await clientes.PreencherAsync(cliente, r) is { } erro) return BadRequest(new { mensagem = erro });
        await db.SaveChangesAsync();
        return ClientesService.Resposta(cliente);
    }

    // POST /api/clientes/5/ativo?valor=false (cliente com fiado em aberto não sai: o fiado cuida disso)
    [HttpPost("{id:int}/ativo")]
    [Authorize(Roles = Gerencia)]
    public async Task<IActionResult> Ativo(int id, bool valor)
    {
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null) return NotFound();
        if (!valor)
        {
            var saldo = await db.LancamentosFiado.Where(l => l.ClienteId == id)
                .SumAsync(l => (decimal?)(l.Tipo == TipoLancamentoFiado.Compra ? l.Valor : -l.Valor)) ?? 0;
            if (saldo > 0) return BadRequest(new { mensagem = "O cliente ainda deve no fiado: receba ou dê baixa antes de desativar." });
        }
        cliente.Ativo = valor;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ----- No caixa (todos os perfis) -----

    // GET /api/clientes/buscar?termo=ana → nome, apelido, telefone ou CPF
    [HttpGet("buscar")]
    public async Task<List<ClienteBuscaPdv>> Buscar(string? termo) =>
        string.IsNullOrWhiteSpace(termo) || termo.Trim().Length < 2 ? [] : await clientes.BuscarNoCaixaAsync(termo);

    // POST /api/clientes/rapido → cadastro em 10 segundos no caixa (telefone/CPF já cadastrado → devolve o mesmo cliente)
    [HttpPost("rapido")]
    public async Task<ActionResult<ClienteBuscaPdv>> Rapido(ClienteRapidoRequest r)
    {
        var (cliente, erro) = await clientes.RapidoAsync(r, UsuarioId);
        if (erro is not null) return BadRequest(new { mensagem = erro });
        var achado = (await clientes.BuscarNoCaixaAsync(cliente!.Telefone ?? cliente.Cpf ?? cliente.Nome)).FirstOrDefault(c => c.Id == cliente.Id);
        return achado ?? new ClienteBuscaPdv(cliente.Id, cliente.Nome, cliente.Apelido, null, 0, null, cliente.Raquete, cliente.Corda, cliente.Tensao, false);
    }
}
