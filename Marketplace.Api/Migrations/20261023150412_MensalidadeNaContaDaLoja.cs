using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class MensalidadeNaContaDaLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Mensalidade",
                table: "Faturas",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PagaPelaLoja",
                table: "Faturas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FaturaId",
                table: "ContasPagar",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContasPagar_FaturaId",
                table: "ContasPagar",
                column: "FaturaId");

            migrationBuilder.AddForeignKey(
                name: "FK_ContasPagar_Faturas_FaturaId",
                table: "ContasPagar",
                column: "FaturaId",
                principalTable: "Faturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContasPagar_Faturas_FaturaId",
                table: "ContasPagar");

            migrationBuilder.DropIndex(
                name: "IX_ContasPagar_FaturaId",
                table: "ContasPagar");

            migrationBuilder.DropColumn(
                name: "Mensalidade",
                table: "Faturas");

            migrationBuilder.DropColumn(
                name: "PagaPelaLoja",
                table: "Faturas");

            migrationBuilder.DropColumn(
                name: "FaturaId",
                table: "ContasPagar");
        }
    }
}
