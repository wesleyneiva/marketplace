using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class FaturaUnicaSoMensalidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Faturas_ClienteId_Competencia",
                table: "Faturas");

            migrationBuilder.CreateIndex(
                name: "IX_Faturas_ClienteId_Competencia",
                table: "Faturas",
                columns: new[] { "ClienteId", "Competencia" },
                unique: true,
                filter: "\"Status\" <> 'Cancelada' AND \"Mensalidade\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Faturas_ClienteId_Competencia",
                table: "Faturas");

            migrationBuilder.CreateIndex(
                name: "IX_Faturas_ClienteId_Competencia",
                table: "Faturas",
                columns: new[] { "ClienteId", "Competencia" },
                unique: true,
                filter: "\"Status\" <> 'Cancelada'");
        }
    }
}
