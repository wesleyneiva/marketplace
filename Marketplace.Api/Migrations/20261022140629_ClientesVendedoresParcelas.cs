using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClientesVendedoresParcelas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recebiveis_SessaoCaixaId_Forma",
                table: "Recebiveis");

            migrationBuilder.AddColumn<int>(
                name: "VendedorId",
                table: "Vendas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Parcela",
                table: "Recebiveis",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Parcelas",
                table: "Recebiveis",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Parcelas",
                table: "PagamentosVenda",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ClienteId",
                table: "OrdensServico",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Corda",
                table: "Clientes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Clientes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Nascimento",
                table: "Clientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Raquete",
                table: "Clientes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Simulado",
                table: "Clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Tensao",
                table: "Clientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ComissaoPercentual",
                table: "Categorias",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vendedores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "pt-BR-x-icu"),
                    ComissaoPercentual = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendedores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vendedores_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComissoesMes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    VendedorId = table.Column<int>(type: "integer", nullable: false),
                    Competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ContaPagarId = table.Column<int>(type: "integer", nullable: false),
                    LancadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComissoesMes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComissoesMes_ContasPagar_ContaPagarId",
                        column: x => x.ContaPagarId,
                        principalTable: "ContasPagar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComissoesMes_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComissoesMes_Vendedores_VendedorId",
                        column: x => x.VendedorId,
                        principalTable: "Vendedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_EmpresaId_ClienteId_DataHora",
                table: "Vendas",
                columns: new[] { "EmpresaId", "ClienteId", "DataHora" },
                filter: "\"ClienteId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_VendedorId",
                table: "Vendas",
                column: "VendedorId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebiveis_SessaoCaixaId_Forma_Parcelas_Parcela",
                table: "Recebiveis",
                columns: new[] { "SessaoCaixaId", "Forma", "Parcelas", "Parcela" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdensServico_ClienteId",
                table: "OrdensServico",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdensServico_EmpresaId_ClienteId",
                table: "OrdensServico",
                columns: new[] { "EmpresaId", "ClienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_EmpresaId_Telefone",
                table: "Clientes",
                columns: new[] { "EmpresaId", "Telefone" });

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesMes_ContaPagarId",
                table: "ComissoesMes",
                column: "ContaPagarId");

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesMes_EmpresaId_VendedorId_Competencia",
                table: "ComissoesMes",
                columns: new[] { "EmpresaId", "VendedorId", "Competencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesMes_VendedorId",
                table: "ComissoesMes",
                column: "VendedorId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendedores_EmpresaId",
                table: "Vendedores",
                column: "EmpresaId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrdensServico_Clientes_ClienteId",
                table: "OrdensServico",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendas_Vendedores_VendedorId",
                table: "Vendas",
                column: "VendedorId",
                principalTable: "Vendedores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrdensServico_Clientes_ClienteId",
                table: "OrdensServico");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendas_Vendedores_VendedorId",
                table: "Vendas");

            migrationBuilder.DropTable(
                name: "ComissoesMes");

            migrationBuilder.DropTable(
                name: "Vendedores");

            migrationBuilder.DropIndex(
                name: "IX_Vendas_EmpresaId_ClienteId_DataHora",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_Vendas_VendedorId",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_Recebiveis_SessaoCaixaId_Forma_Parcelas_Parcela",
                table: "Recebiveis");

            migrationBuilder.DropIndex(
                name: "IX_OrdensServico_ClienteId",
                table: "OrdensServico");

            migrationBuilder.DropIndex(
                name: "IX_OrdensServico_EmpresaId_ClienteId",
                table: "OrdensServico");

            migrationBuilder.DropIndex(
                name: "IX_Clientes_EmpresaId_Telefone",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "VendedorId",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "Parcela",
                table: "Recebiveis");

            migrationBuilder.DropColumn(
                name: "Parcelas",
                table: "Recebiveis");

            migrationBuilder.DropColumn(
                name: "Parcelas",
                table: "PagamentosVenda");

            migrationBuilder.DropColumn(
                name: "ClienteId",
                table: "OrdensServico");

            migrationBuilder.DropColumn(
                name: "Corda",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Nascimento",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Raquete",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Simulado",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Tensao",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "ComissaoPercentual",
                table: "Categorias");

            migrationBuilder.CreateIndex(
                name: "IX_Recebiveis_SessaoCaixaId_Forma",
                table: "Recebiveis",
                columns: new[] { "SessaoCaixaId", "Forma" },
                unique: true);
        }
    }
}
