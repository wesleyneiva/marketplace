using System.ComponentModel.DataAnnotations;

namespace Marketplace.Api.Contracts;

// Tela "Empresas" (só o dono da plataforma).

public record EmpresaResumoResponse(
    int Id, string Nome, string Subdominio, int LimiteCaixas, bool Ativa, bool Demonstracao, DateTimeOffset CriadoEm,
    int Usuarios, int UsuariosAtivos, int Produtos, int Vendas30Dias, decimal Faturamento30Dias,
    DateTimeOffset? UltimoAcesso, DateTimeOffset? UltimaVenda, int CaixasAbertos, string? Cidade);

public record NovaEmpresaRequest(
    [Required(ErrorMessage = "Informe o nome da empresa.")][StringLength(100, MinimumLength = 2)] string Nome,
    [StringLength(40)] string? Subdominio,
    [Range(1, 999, ErrorMessage = "Caixas: de 1 a 999.")] int LimiteCaixas,
    [Required(ErrorMessage = "Informe o nome do administrador.")][StringLength(100, MinimumLength = 2)] string AdminNome,
    [Required(ErrorMessage = "Informe o e-mail do administrador.")][EmailAddress(ErrorMessage = "E-mail inválido.")][StringLength(150)] string AdminEmail,
    CidadeRequest? Cidade = null);

public record EditarEmpresaRequest(
    [Required][StringLength(100, MinimumLength = 2)] string Nome,
    [Range(1, 999, ErrorMessage = "Caixas: de 1 a 999.")] int LimiteCaixas);

public record EmpresaCriadaResponse(EmpresaResumoResponse Empresa, string AdminEmail, string SenhaProvisoria);

public record UsuarioDaEmpresaResponse(
    string Id, string Nome, string Email, IList<string> Perfis, bool Ativo, bool TrocarSenha, bool Bloqueado,
    DateTimeOffset? UltimoAcesso);

public record SenhaRedefinidaResponse(string Email, string SenhaProvisoria);
