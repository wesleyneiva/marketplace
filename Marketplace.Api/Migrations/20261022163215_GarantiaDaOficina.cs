using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class GarantiaDaOficina : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CordaGarantiaId",
                table: "OrdensServico",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CustoGarantia",
                table: "OrdensServico",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "GarantiaDeId",
                table: "OrdensServico",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoGarantia",
                table: "OrdensServico",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdensServico_GarantiaDeId",
                table: "OrdensServico",
                column: "GarantiaDeId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrdensServico_OrdensServico_GarantiaDeId",
                table: "OrdensServico",
                column: "GarantiaDeId",
                principalTable: "OrdensServico",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrdensServico_OrdensServico_GarantiaDeId",
                table: "OrdensServico");

            migrationBuilder.DropIndex(
                name: "IX_OrdensServico_GarantiaDeId",
                table: "OrdensServico");

            migrationBuilder.DropColumn(
                name: "CordaGarantiaId",
                table: "OrdensServico");

            migrationBuilder.DropColumn(
                name: "CustoGarantia",
                table: "OrdensServico");

            migrationBuilder.DropColumn(
                name: "GarantiaDeId",
                table: "OrdensServico");

            migrationBuilder.DropColumn(
                name: "MotivoGarantia",
                table: "OrdensServico");
        }
    }
}
