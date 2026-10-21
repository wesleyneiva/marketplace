using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// Área do dono da plataforma: os CLIENTES do sistema. Só USO (acessos, dias com movimento, cadastros) — nunca
// quanto cada cliente vende ou fatura: isso é sigiloso do cliente.

public record EmpresaResumoResponse(
    int Id, string Nome, string Subdominio, int LimiteCaixas, bool Ativa, bool Demonstracao, DateTimeOffset CriadoEm,
    int Usuarios, int UsuariosAtivos, int Produtos, int DiasComMovimento30,
    DateTimeOffset? UltimoAcesso, DateTimeOffset? UltimoMovimento, int CaixasAbertos, string? Cidade, string VendasAutomaticas,
    string? Responsavel, string? Telefone, string? Plano, decimal ValorMensal, bool Cortesia, string? MotivoSuspensao,
    int FaturasAtrasadas, int MaiorAtraso, string Ramo);

// Ficha cadastral do cliente (só o dono vê e muda).
public record FichaCliente(
    [StringLength(150)] string? RazaoSocial,
    [StringLength(18)] string? Documento,
    [StringLength(100)] string? Responsavel,
    [StringLength(20)] string? Telefone,
    [EmailAddress(ErrorMessage = "E-mail de contato inválido.")][StringLength(150)] string? EmailContato,
    [StringLength(250)] string? Endereco,
    [StringLength(1000)] string? Observacoes);

// Um cliente aberto na área do dono: resumo + ficha + configurações da loja.
public record ClienteDetalheResponse(
    EmpresaResumoResponse Resumo, FichaCliente Ficha, string? Uf, string Fuso, Marketplace.Api.Models.HorarioFuncionamento Horario,
    string HorarioResumo, AssinaturaResponse Assinatura,
    string[]? Modulos); // módulos liberados; null = todos

// Módulos liberados para o cliente: todos (inclusive os que vierem) ou só os da lista (ver Models/Modulos.cs).
public record ModulosRequest(bool Todos, string[]? Modulos);

public record VisaoGeralResponse(
    int Clientes, int Ativos, int Suspensos, int Demonstracoes, int CaixasContratados, int UsuariosAtivos,
    int AcessaramHoje, int AcessaramSemana, List<EmpresaResumoResponse> SemAcesso, List<EmpresaResumoResponse> Recentes,
    FaturamentoPlataforma Faturamento);

public record NovaEmpresaRequest(
    [Required(ErrorMessage = "Informe o nome da empresa.")][StringLength(100, MinimumLength = 2)] string Nome,
    [StringLength(40)] string? Subdominio,
    [Range(1, 999, ErrorMessage = "Caixas: de 1 a 999.")] int LimiteCaixas,
    [Required(ErrorMessage = "Informe o nome do administrador.")][StringLength(100, MinimumLength = 2)] string AdminNome,
    [Required(ErrorMessage = "Informe o e-mail do administrador.")][EmailAddress(ErrorMessage = "E-mail inválido.")][StringLength(150)] string AdminEmail,
    CidadeRequest? Cidade = null,
    string? Ramo = null, // null = mercado
    FichaCliente? Ficha = null);

public record EditarEmpresaRequest(
    [Required][StringLength(100, MinimumLength = 2)] string Nome,
    [Range(1, 999, ErrorMessage = "Caixas: de 1 a 999.")] int LimiteCaixas,
    string? VendasAutomaticas = null, // null = não muda
    string? Ramo = null,              // null = não muda
    FichaCliente? Ficha = null);      // null = não muda

public record EmpresaCriadaResponse(EmpresaResumoResponse Empresa, string AdminEmail, string SenhaProvisoria);

public record UsuarioDaEmpresaResponse(
    string Id, string Nome, string Email, IList<string> Perfis, bool Ativo, bool TrocarSenha, bool Bloqueado,
    DateTimeOffset? UltimoAcesso);

public record SenhaRedefinidaResponse(string Email, string SenhaProvisoria);
