using System.Text.RegularExpressions;
using Marketplace.Api.Controllers;
using Marketplace.Api.Models;
using Marketplace.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// Cadastro de um CLIENTE novo (uma empresa), pela linha de comando da VM — só quem tem acesso à VM consegue:
//
//   ./nova-empresa.sh --nome "Mercado do Zé" --subdominio mercadoze --caixas 2 \
//                     --admin-email ze@mercadoze.com.br --admin-nome "José da Silva"
//
// Cria a empresa, as categorias padrão (vazias) e o ADMINISTRADOR dela com senha provisória
// (mostrada uma vez; ele troca no primeiro acesso). Daí em diante, o próprio cliente cadastra o resto.
public static partial class NovaEmpresa
{
    public const string Comando = "nova-empresa";

    private static readonly string[] CategoriasPadrao =
        ["Mercearia", "Bebidas", "Hortifrúti", "Padaria", "Açougue", "Frios e Laticínios", "Limpeza", "Higiene e Beleza"];

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex SubdominioValido();

    public class NovaEmpresaException(string mensagem) : Exception(mensagem);

    // Pela linha de comando (nova-empresa.sh).
    public static async Task<int> ExecutarAsync(IServiceProvider services, IConfiguration args)
    {
        var nome = args["nome"]?.Trim();
        var subdominio = args["subdominio"]?.Trim().ToLowerInvariant();
        var email = args["admin-email"]?.Trim();
        var nomeAdmin = args["admin-nome"]?.Trim();
        if (string.IsNullOrEmpty(nome) || string.IsNullOrEmpty(subdominio) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(nomeAdmin))
            return Erro("Uso: nova-empresa --nome \"Mercado do Zé\" --subdominio mercadoze --caixas 2 --admin-email ze@exemplo.com --admin-nome \"José\"");

        try
        {
            var caixas = args.GetValue("caixas", 2);
            var (empresaId, senha) = await CriarAsync(services, nome, subdominio, caixas, email, nomeAdmin);
            Console.WriteLine($"""

                ✅ Empresa criada: #{empresaId} {nome}
                   Apelido interno: {subdominio}
                   Caixas: {caixas}
                   Categorias padrão: {CategoriasPadrao.Length}

                   Administrador: {nomeAdmin} <{email}>
                   Senha provisória: {senha}   ← anote e passe ao cliente (troca no 1º acesso)
                   Endereço: https://app.wnlabs.com.br

                """);
            return 0;
        }
        catch (NovaEmpresaException e)
        {
            return Erro(e.Message);
        }
    }

    // O cadastro em si (usado pelo comando e pela tela "Empresas" do dono da plataforma).
    // Devolve o id da empresa e a senha provisória do administrador (mostrada uma vez só).
    public static async Task<(int EmpresaId, string Senha)> CriarAsync(
        IServiceProvider services, string nome, string subdominio, int caixas, string email, string nomeAdmin,
        Contracts.CidadeRequest? cidade = null)
    {
        nome = nome.Trim();
        subdominio = subdominio.Trim().ToLowerInvariant();
        email = email.Trim();
        nomeAdmin = nomeAdmin.Trim();
        if (nome.Length is < 2 or > 100) throw new NovaEmpresaException("Nome da empresa: de 2 a 100 letras.");
        if (!SubdominioValido().IsMatch(subdominio))
            throw new NovaEmpresaException("Apelido: só letras minúsculas, números e hífen (ex.: mercadoze).");
        if (caixas is < 1 or > 999) throw new NovaEmpresaException("Caixas: de 1 a 999.");
        if (nomeAdmin.Length < 2) throw new NovaEmpresaException("Informe o nome do administrador.");
        if (!email.Contains('@') || email.Length > 150) throw new NovaEmpresaException("E-mail do administrador inválido.");

        int empresaId;
        using (var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Empresas.AnyAsync(e => e.Subdominio == subdominio))
                throw new NovaEmpresaException($"Já existe uma empresa com o apelido \"{subdominio}\".");
            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == usuarios.NormalizeEmail(email)))
                throw new NovaEmpresaException($"O e-mail {email} já é usuário do sistema.");

            var empresa = new Empresa { Nome = nome, Subdominio = subdominio, LimiteCaixas = caixas };
            if (cidade is not null && Services.Geografia.FusoValido(cidade.Fuso))
            {
                (empresa.Cidade, empresa.Uf, empresa.Latitude, empresa.Longitude, empresa.Fuso) =
                    (cidade.Nome.Trim(), cidade.Uf.Trim().ToUpperInvariant(), Math.Round(cidade.Latitude, 5), Math.Round(cidade.Longitude, 5), cidade.Fuso);
            }
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            empresaId = empresa.Id;
        }

        // Daqui em diante, tudo DENTRO da empresa nova (o carimbo automático põe o EmpresaId certo).
        using (var scope = services.CriarEscopo(empresaId))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Categorias.AddRange(CategoriasPadrao.Select(c => new Categoria { Nome = c }));
            await db.SaveChangesAsync();

            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var senha = UsuariosController.GerarSenhaProvisoria();
            var admin = new Usuario
            {
                UserName = email, Email = email, EmailConfirmed = true, NomeCompleto = nomeAdmin, TrocarSenha = true,
            };
            var criado = await usuarios.CreateAsync(admin, senha);
            if (!criado.Succeeded)
                throw new NovaEmpresaException("Empresa criada, mas o administrador não: " + string.Join(" ", criado.Errors.Select(AuthController.TraduzirErro)));
            await usuarios.AddToRoleAsync(admin, Perfis.Administrador);
            return (empresaId, senha);
        }
    }

    // "Mercado do Zé" → "mercado-do-ze" (sugestão de apelido na tela).
    public static string Apelido(string nome)
    {
        var semAcento = Services.ImportacaoProdutosService.Normalizar(nome);
        var apelido = Regex.Replace(semAcento, "[^a-z0-9]+", "-").Trim('-');
        return apelido.Length > 40 ? apelido[..40].Trim('-') : apelido;
    }

    private static int Erro(string mensagem)
    {
        Console.Error.WriteLine("❌ " + mensagem);
        return 1;
    }
}
