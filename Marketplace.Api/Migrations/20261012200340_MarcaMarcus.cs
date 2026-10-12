using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class MarcaMarcus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nome comercial: MARCUS (12/10/2026). Tira "Marketplace" e "fictício" dos DADOS.
            // E-mails: o domínio marketplace.com é de outra pessoa; os logins internos passam para wnlabs.com.br / sistema.local.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE troca_email (antigo text, novo text);
                INSERT INTO troca_email VALUES
                  ('simulador@marketplace.local', 'caixa-automatico-1@sistema.local'),
                  ('visitante@marketplace.demo', 'visitante@demo.wnlabs.com.br'),
                  ('admin@marketplace.com', 'admin.demo@wnlabs.com.br'),
                  ('teste@marketplace.com', 'teste.demo@wnlabs.com.br'),
                  ('testecliente@marketplace.com', 'testecliente@wnlabs.com.br');
                UPDATE "AspNetUsers" u
                   SET "Email" = t.novo, "UserName" = t.novo, "NormalizedEmail" = upper(t.novo), "NormalizedUserName" = upper(t.novo)
                  FROM troca_email t
                 WHERE u."Email" = t.antigo
                   AND NOT EXISTS (SELECT 1 FROM "AspNetUsers" x WHERE x."NormalizedEmail" = upper(t.novo));
                DROP TABLE troca_email;

                UPDATE "Empresas" SET "Nome" = 'Mercado Demonstração' WHERE "Demonstracao" AND "Nome" = 'Marketplace';
                UPDATE "Fornecedores" SET "Nome" = regexp_replace("Nome", '\s*\(fict[íi]ci[ao]\)', '', 'g') WHERE "Nome" ~* 'fict[íi]ci';
                UPDATE "Clientes" SET "Observacao" = replace("Observacao", 'Cliente fictício (demonstração)', 'Cliente de demonstração')
                 WHERE "Observacao" LIKE 'Cliente fictício (demonstração)%';
                UPDATE "ContasPagar" SET "Descricao" = replace("Descricao", 'Sistema Marketplace', 'Sistema Marcus') WHERE "Descricao" LIKE '%Sistema Marketplace%';
                UPDATE "MovimentosFinanceiros" SET "Descricao" = replace("Descricao", 'Sistema Marketplace', 'Sistema Marcus') WHERE "Descricao" LIKE '%Sistema Marketplace%';
                """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
