using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class CidadeDaLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clima_DataHora",
                table: "Clima");

            migrationBuilder.AddColumn<string>(
                name: "Cidade",
                table: "Empresas",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fuso",
                table: "Empresas",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "America/Sao_Paulo");

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Empresas",
                type: "numeric(8,5)",
                precision: 8,
                scale: 5,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Empresas",
                type: "numeric(8,5)",
                precision: 8,
                scale: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Uf",
                table: "Empresas",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Clima",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Clima",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Tudo o que existia até aqui é de Porto Alegre: o clima medido e as empresas (cada uma muda depois em Configurações).
            migrationBuilder.Sql("UPDATE \"Clima\" SET \"Latitude\" = -30.03, \"Longitude\" = -51.23;");
            migrationBuilder.Sql("UPDATE \"Empresas\" SET \"Cidade\" = 'Porto Alegre', \"Uf\" = 'RS', \"Latitude\" = -30.03, \"Longitude\" = -51.23;");

            migrationBuilder.CreateIndex(
                name: "IX_Clima_Latitude_Longitude_DataHora",
                table: "Clima",
                columns: new[] { "Latitude", "Longitude", "DataHora" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clima_Latitude_Longitude_DataHora",
                table: "Clima");

            migrationBuilder.DropColumn(
                name: "Cidade",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Fuso",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Uf",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Clima");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Clima");

            migrationBuilder.CreateIndex(
                name: "IX_Clima_DataHora",
                table: "Clima",
                column: "DataHora",
                unique: true);
        }
    }
}
