using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class NotasEntrada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotasEntrada",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Chave = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    Numero = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    Serie = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DataEmissao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValorTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    QuantidadeItens = table.Column<int>(type: "integer", nullable: false),
                    FornecedorId = table.Column<int>(type: "integer", nullable: false),
                    PedidoCompraId = table.Column<int>(type: "integer", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: false),
                    RegistradaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegistradaPorId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotasEntrada", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotasEntrada_AspNetUsers_RegistradaPorId",
                        column: x => x.RegistradaPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotasEntrada_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotasEntrada_Fornecedores_FornecedorId",
                        column: x => x.FornecedorId,
                        principalTable: "Fornecedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotasEntrada_PedidosCompra_PedidoCompraId",
                        column: x => x.PedidoCompraId,
                        principalTable: "PedidosCompra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VinculosFornecedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    FornecedorId = table.Column<int>(type: "integer", nullable: false),
                    CodigoFornecedor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProdutoId = table.Column<int>(type: "integer", nullable: false),
                    Fator = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VinculosFornecedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VinculosFornecedor_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VinculosFornecedor_Fornecedores_FornecedorId",
                        column: x => x.FornecedorId,
                        principalTable: "Fornecedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VinculosFornecedor_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotasEntrada_DataEmissao",
                table: "NotasEntrada",
                column: "DataEmissao");

            migrationBuilder.CreateIndex(
                name: "IX_NotasEntrada_EmpresaId_Chave",
                table: "NotasEntrada",
                columns: new[] { "EmpresaId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotasEntrada_FornecedorId",
                table: "NotasEntrada",
                column: "FornecedorId");

            migrationBuilder.CreateIndex(
                name: "IX_NotasEntrada_PedidoCompraId",
                table: "NotasEntrada",
                column: "PedidoCompraId");

            migrationBuilder.CreateIndex(
                name: "IX_NotasEntrada_RegistradaPorId",
                table: "NotasEntrada",
                column: "RegistradaPorId");

            migrationBuilder.CreateIndex(
                name: "IX_VinculosFornecedor_EmpresaId_FornecedorId_CodigoFornecedor",
                table: "VinculosFornecedor",
                columns: new[] { "EmpresaId", "FornecedorId", "CodigoFornecedor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VinculosFornecedor_FornecedorId",
                table: "VinculosFornecedor",
                column: "FornecedorId");

            migrationBuilder.CreateIndex(
                name: "IX_VinculosFornecedor_ProdutoId",
                table: "VinculosFornecedor",
                column: "ProdutoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotasEntrada");

            migrationBuilder.DropTable(
                name: "VinculosFornecedor");
        }
    }
}
