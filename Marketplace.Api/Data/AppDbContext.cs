using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// O DbContext é a "ponte" entre o C# e o banco de dados.
// Cada tabela vai virar uma propriedade DbSet<...> aqui (ex.: Produtos, Vendas).
// Por enquanto está vazio: as primeiras tabelas chegam no passo 8 (login/usuários).
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
