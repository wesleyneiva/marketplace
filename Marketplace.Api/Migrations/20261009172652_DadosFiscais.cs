using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class DadosFiscais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cest",
                table: "Produtos",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cfop",
                table: "Produtos",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "5102");

            migrationBuilder.AddColumn<string>(
                name: "Ncm",
                table: "Produtos",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origem",
                table: "Produtos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SituacaoTributaria",
                table: "Produtos",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cest",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Cfop",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Ncm",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Origem",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "SituacaoTributaria",
                table: "Produtos");
        }
    }
}
