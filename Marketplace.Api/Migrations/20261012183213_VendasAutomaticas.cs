using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class VendasAutomaticas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendasAutomaticas",
                table: "Empresas",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Desligado");

            // A demonstração continua com o bot completo (como antes, quando ele só existia para ela).
            migrationBuilder.Sql("""UPDATE "Empresas" SET "VendasAutomaticas" = 'Completo' WHERE "Demonstracao";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendasAutomaticas",
                table: "Empresas");
        }
    }
}
