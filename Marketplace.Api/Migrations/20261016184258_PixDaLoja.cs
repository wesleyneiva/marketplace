using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class PixDaLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PixChave",
                table: "Empresas",
                type: "character varying(77)",
                maxLength: 77,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixNomeRecebedor",
                table: "Empresas",
                type: "character varying(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixTipoChave",
                table: "Empresas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PixChave",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PixNomeRecebedor",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PixTipoChave",
                table: "Empresas");
        }
    }
}
