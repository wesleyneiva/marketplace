using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class PixAutomatico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CobrancaPixId",
                table: "PagamentosVenda",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoConta",
                table: "Empresas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MercadoPagoTeste",
                table: "Empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoToken",
                table: "Empresas",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoTokenFinal",
                table: "Empresas",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixAutomatico",
                table: "Empresas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixAvisoToken",
                table: "Empresas",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CobrancasPix",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Provedor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IdExterno = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Referencia = table.Column<Guid>(type: "uuid", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CopiaECola = table.Column<string>(type: "text", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PagaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsultadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StatusExterno = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SessaoCaixaId = table.Column<int>(type: "integer", nullable: true),
                    CriadaPorId = table.Column<string>(type: "text", nullable: true),
                    VendaId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CobrancasPix", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CobrancasPix_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CobrancasPix_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CobrancasPix_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_PixAvisoToken",
                table: "Empresas",
                column: "PixAvisoToken",
                unique: true,
                filter: "\"PixAvisoToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasPix_EmpresaId",
                table: "CobrancasPix",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasPix_Provedor_IdExterno",
                table: "CobrancasPix",
                columns: new[] { "Provedor", "IdExterno" },
                unique: true,
                filter: "\"IdExterno\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasPix_Referencia",
                table: "CobrancasPix",
                column: "Referencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasPix_SessaoCaixaId_Status",
                table: "CobrancasPix",
                columns: new[] { "SessaoCaixaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasPix_VendaId",
                table: "CobrancasPix",
                column: "VendaId",
                unique: true,
                filter: "\"VendaId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CobrancasPix");

            migrationBuilder.DropIndex(
                name: "IX_Empresas_PixAvisoToken",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "CobrancaPixId",
                table: "PagamentosVenda");

            migrationBuilder.DropColumn(
                name: "MercadoPagoConta",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "MercadoPagoTeste",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "MercadoPagoToken",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "MercadoPagoTokenFinal",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PixAutomatico",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PixAvisoToken",
                table: "Empresas");
        }
    }
}
