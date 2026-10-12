using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class AreaDoDono : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Documento",
                table: "Empresas",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailContato",
                table: "Empresas",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Endereco",
                table: "Empresas",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacoes",
                table: "Empresas",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Plataforma",
                table: "Empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RazaoSocial",
                table: "Empresas",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Responsavel",
                table: "Empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefone",
                table: "Empresas",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_Plataforma",
                table: "Empresas",
                column: "Plataforma",
                unique: true,
                filter: "\"Plataforma\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Empresas_Plataforma",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Documento",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "EmailContato",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Endereco",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Observacoes",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Plataforma",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "RazaoSocial",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Responsavel",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Telefone",
                table: "Empresas");
        }
    }
}
