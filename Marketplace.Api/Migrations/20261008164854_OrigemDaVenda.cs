using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrigemDaVenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origem",
                table: "Vendas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Caixa");

            // Vendas que o simulador ao vivo já tinha feito antes desta coluna existir.
            migrationBuilder.Sql("""
                UPDATE "Vendas" SET "Origem" = 'Simulador'
                WHERE "UsuarioId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "Email" = 'simulador@marketplace.local');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_Origem_DataHora",
                table: "Vendas",
                columns: new[] { "Origem", "DataHora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_Origem_DataHora",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "Origem",
                table: "Vendas");
        }
    }
}
