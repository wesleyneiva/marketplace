using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class IndicesRelatorios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_EmpresaId_DataHora",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_PagamentosVenda_VendaId",
                table: "PagamentosVenda");

            migrationBuilder.DropIndex(
                name: "IX_ItensVenda_VendaId",
                table: "ItensVenda");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_EmpresaId_DataHora",
                table: "Vendas",
                columns: new[] { "EmpresaId", "DataHora" })
                .Annotation("Npgsql:IndexInclude", new[] { "Status", "Total", "Troco" });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosVenda_VendaId",
                table: "PagamentosVenda",
                column: "VendaId")
                .Annotation("Npgsql:IndexInclude", new[] { "Forma", "Valor" });

            migrationBuilder.CreateIndex(
                name: "IX_ItensVenda_VendaId",
                table: "ItensVenda",
                column: "VendaId")
                .Annotation("Npgsql:IndexInclude", new[] { "ProdutoId", "Quantidade", "CustoUnitario", "Total" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_EmpresaId_DataHora",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_PagamentosVenda_VendaId",
                table: "PagamentosVenda");

            migrationBuilder.DropIndex(
                name: "IX_ItensVenda_VendaId",
                table: "ItensVenda");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_EmpresaId_DataHora",
                table: "Vendas",
                columns: new[] { "EmpresaId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosVenda_VendaId",
                table: "PagamentosVenda",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVenda_VendaId",
                table: "ItensVenda",
                column: "VendaId");
        }
    }
}
