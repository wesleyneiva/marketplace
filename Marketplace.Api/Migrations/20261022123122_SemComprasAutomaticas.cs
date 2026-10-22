using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class SemComprasAutomaticas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Os rascunhos (montados sozinhos ou à mão) e os automáticos que foram cancelados na troca do dia somem:
            // daqui em diante pedido só existe depois de enviado. Os itens vão junto (cascata).
            migrationBuilder.Sql("""
                DELETE FROM "PedidosCompra" p
                WHERE (p."Status" = 'Rascunho' OR (p."Status" = 'Cancelado' AND p."SugestaoAutomatica"))
                  AND NOT EXISTS (SELECT 1 FROM "ContasPagar" c WHERE c."PedidoCompraId" = p."Id");
                """);

            migrationBuilder.DropColumn(
                name: "SugestaoAutomatica",
                table: "PedidosCompra");

            migrationBuilder.DropColumn(
                name: "ComprasAutomaticas",
                table: "Empresas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
