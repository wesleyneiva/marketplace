using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class PixPeloBanco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PixBancoCertificado",
                table: "Empresas",
                type: "character varying(40000)",
                maxLength: 40000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixBancoCertificadoInfo",
                table: "Empresas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixBancoClientId",
                table: "Empresas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixBancoSegredos",
                table: "Empresas",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PixBancoTeste",
                table: "Empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EndToEndId",
                table: "CobrancasPix",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PixBancoCertificado",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PixBancoCertificadoInfo",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PixBancoClientId",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PixBancoSegredos",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PixBancoTeste",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "EndToEndId",
                table: "CobrancasPix");
        }
    }
}
