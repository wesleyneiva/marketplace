using Marketplace.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Data;

// O DbContext é a "ponte" entre o C# e o banco de dados.
// Herdando de IdentityDbContext, ganhamos prontas as tabelas de usuários, perfis e vínculos.
// As tabelas do mercado (Produtos, Vendas...) vão virar propriedades DbSet<...> aqui.
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<Usuario, IdentityRole, string>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Usuario>()
            .Property(u => u.NomeCompleto)
            .HasMaxLength(150);
    }
}
