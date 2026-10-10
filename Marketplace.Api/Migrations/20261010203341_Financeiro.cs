using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class Financeiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoriaFinanceiraId",
                table: "ContasPagar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContaFinanceiraId",
                table: "ContasPagar",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoriasFinanceiras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "pt-BR-x-icu"),
                    Grupo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Natureza = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Chave = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriasFinanceiras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoriasFinanceiras_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContasFinanceiras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "pt-BR-x-icu"),
                    Tipo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Banco = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Agencia = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SaldoInicial = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    SaldoInicialEm = table.Column<DateOnly>(type: "date", nullable: false),
                    Chave = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContasFinanceiras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContasFinanceiras_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosFinanceiros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ContaFinanceiraId = table.Column<int>(type: "integer", nullable: false),
                    CategoriaFinanceiraId = table.Column<int>(type: "integer", nullable: true),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Documento = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Origem = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    ContaPagarId = table.Column<int>(type: "integer", nullable: true),
                    TransferenciaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Conciliado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosFinanceiros", x => x.Id);
                    table.CheckConstraint("CK_MovimentosFinanceiros_ValorPositivo", "\"Valor\" > 0");
                    table.ForeignKey(
                        name: "FK_MovimentosFinanceiros_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosFinanceiros_CategoriasFinanceiras_CategoriaFinanc~",
                        column: x => x.CategoriaFinanceiraId,
                        principalTable: "CategoriasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosFinanceiros_ContasFinanceiras_ContaFinanceiraId",
                        column: x => x.ContaFinanceiraId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosFinanceiros_ContasPagar_ContaPagarId",
                        column: x => x.ContaPagarId,
                        principalTable: "ContasPagar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosFinanceiros_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContasPagar_CategoriaFinanceiraId",
                table: "ContasPagar",
                column: "CategoriaFinanceiraId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasPagar_ContaFinanceiraId",
                table: "ContasPagar",
                column: "ContaFinanceiraId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasFinanceiras_EmpresaId_Chave",
                table: "CategoriasFinanceiras",
                columns: new[] { "EmpresaId", "Chave" },
                unique: true,
                filter: "\"Chave\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasFinanceiras_EmpresaId_Codigo",
                table: "CategoriasFinanceiras",
                columns: new[] { "EmpresaId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasFinanceiras_EmpresaId_Nome",
                table: "CategoriasFinanceiras",
                columns: new[] { "EmpresaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContasFinanceiras_EmpresaId_Chave",
                table: "ContasFinanceiras",
                columns: new[] { "EmpresaId", "Chave" },
                unique: true,
                filter: "\"Chave\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContasFinanceiras_EmpresaId_Nome",
                table: "ContasFinanceiras",
                columns: new[] { "EmpresaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_CategoriaFinanceiraId",
                table: "MovimentosFinanceiros",
                column: "CategoriaFinanceiraId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_ContaFinanceiraId_Data",
                table: "MovimentosFinanceiros",
                columns: new[] { "ContaFinanceiraId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_ContaPagarId",
                table: "MovimentosFinanceiros",
                column: "ContaPagarId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_CriadoPorId",
                table: "MovimentosFinanceiros",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_EmpresaId_Data",
                table: "MovimentosFinanceiros",
                columns: new[] { "EmpresaId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_TransferenciaId",
                table: "MovimentosFinanceiros",
                column: "TransferenciaId");

            migrationBuilder.AddForeignKey(
                name: "FK_ContasPagar_CategoriasFinanceiras_CategoriaFinanceiraId",
                table: "ContasPagar",
                column: "CategoriaFinanceiraId",
                principalTable: "CategoriasFinanceiras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContasPagar_ContasFinanceiras_ContaFinanceiraId",
                table: "ContasPagar",
                column: "ContaFinanceiraId",
                principalTable: "ContasFinanceiras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContasPagar_CategoriasFinanceiras_CategoriaFinanceiraId",
                table: "ContasPagar");

            migrationBuilder.DropForeignKey(
                name: "FK_ContasPagar_ContasFinanceiras_ContaFinanceiraId",
                table: "ContasPagar");

            migrationBuilder.DropTable(
                name: "MovimentosFinanceiros");

            migrationBuilder.DropTable(
                name: "CategoriasFinanceiras");

            migrationBuilder.DropTable(
                name: "ContasFinanceiras");

            migrationBuilder.DropIndex(
                name: "IX_ContasPagar_CategoriaFinanceiraId",
                table: "ContasPagar");

            migrationBuilder.DropIndex(
                name: "IX_ContasPagar_ContaFinanceiraId",
                table: "ContasPagar");

            migrationBuilder.DropColumn(
                name: "CategoriaFinanceiraId",
                table: "ContasPagar");

            migrationBuilder.DropColumn(
                name: "ContaFinanceiraId",
                table: "ContasPagar");
        }
    }
}
