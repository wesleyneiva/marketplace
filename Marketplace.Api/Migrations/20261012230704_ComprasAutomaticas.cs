using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class ComprasAutomaticas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SugestaoAutomatica",
                table: "PedidosCompra",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ComprasAutomaticas",
                table: "Empresas",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SugestaoAutomatica",
                table: "PedidosCompra");

            migrationBuilder.DropColumn(
                name: "ComprasAutomaticas",
                table: "Empresas");
        }
    }
}
