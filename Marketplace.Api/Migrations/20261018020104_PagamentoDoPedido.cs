using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class PagamentoDoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pagamento",
                table: "PedidosCompra",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PedidoCompraId",
                table: "ContasPagar",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContasPagar_PedidoCompraId",
                table: "ContasPagar",
                column: "PedidoCompraId");

            migrationBuilder.AddForeignKey(
                name: "FK_ContasPagar_PedidosCompra_PedidoCompraId",
                table: "ContasPagar",
                column: "PedidoCompraId",
                principalTable: "PedidosCompra",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContasPagar_PedidosCompra_PedidoCompraId",
                table: "ContasPagar");

            migrationBuilder.DropIndex(
                name: "IX_ContasPagar_PedidoCompraId",
                table: "ContasPagar");

            migrationBuilder.DropColumn(
                name: "Pagamento",
                table: "PedidosCompra");

            migrationBuilder.DropColumn(
                name: "PedidoCompraId",
                table: "ContasPagar");
        }
    }
}
