using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class Balanca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CodigoBalanca",
                table: "Produtos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BalancaDigitosCodigo",
                table: "Empresas",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<string>(
                name: "BalancaEtiqueta",
                table: "Empresas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Preco");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_EmpresaId_CodigoBalanca",
                table: "Produtos",
                columns: new[] { "EmpresaId", "CodigoBalanca" },
                unique: true,
                filter: "\"CodigoBalanca\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Produtos_EmpresaId_CodigoBalanca",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "CodigoBalanca",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "BalancaDigitosCodigo",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "BalancaEtiqueta",
                table: "Empresas");
        }
    }
}
