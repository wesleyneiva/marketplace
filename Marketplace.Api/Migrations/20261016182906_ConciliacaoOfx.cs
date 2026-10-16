using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class ConciliacaoOfx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConciliacaoId",
                table: "MovimentosFinanceiros",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExtratosBancarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    ContaFinanceiraId = table.Column<int>(type: "integer", nullable: false),
                    NomeArquivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Banco = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Agencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NumeroConta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    De = table.Column<DateOnly>(type: "date", nullable: false),
                    Ate = table.Column<DateOnly>(type: "date", nullable: false),
                    SaldoFinal = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    SaldoEm = table.Column<DateOnly>(type: "date", nullable: true),
                    Linhas = table.Column<int>(type: "integer", nullable: false),
                    Novas = table.Column<int>(type: "integer", nullable: false),
                    ImportadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportadoPorId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtratosBancarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtratosBancarios_AspNetUsers_ImportadoPorId",
                        column: x => x.ImportadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExtratosBancarios_ContasFinanceiras_ContaFinanceiraId",
                        column: x => x.ContaFinanceiraId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExtratosBancarios_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LinhasExtrato",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    ExtratoBancarioId = table.Column<int>(type: "integer", nullable: false),
                    ContaFinanceiraId = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Documento = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    TipoBanco = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Chave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ConciliacaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConciliadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConciliadaPorId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinhasExtrato", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinhasExtrato_ContasFinanceiras_ContaFinanceiraId",
                        column: x => x.ContaFinanceiraId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LinhasExtrato_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LinhasExtrato_ExtratosBancarios_ExtratoBancarioId",
                        column: x => x.ExtratoBancarioId,
                        principalTable: "ExtratosBancarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_ConciliacaoId",
                table: "MovimentosFinanceiros",
                column: "ConciliacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtratosBancarios_ContaFinanceiraId_ImportadoEm",
                table: "ExtratosBancarios",
                columns: new[] { "ContaFinanceiraId", "ImportadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtratosBancarios_EmpresaId",
                table: "ExtratosBancarios",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtratosBancarios_ImportadoPorId",
                table: "ExtratosBancarios",
                column: "ImportadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_LinhasExtrato_ConciliacaoId",
                table: "LinhasExtrato",
                column: "ConciliacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_LinhasExtrato_ContaFinanceiraId_Chave",
                table: "LinhasExtrato",
                columns: new[] { "ContaFinanceiraId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinhasExtrato_ContaFinanceiraId_Status_Data",
                table: "LinhasExtrato",
                columns: new[] { "ContaFinanceiraId", "Status", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_LinhasExtrato_EmpresaId",
                table: "LinhasExtrato",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_LinhasExtrato_ExtratoBancarioId",
                table: "LinhasExtrato",
                column: "ExtratoBancarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinhasExtrato");

            migrationBuilder.DropTable(
                name: "ExtratosBancarios");

            migrationBuilder.DropIndex(
                name: "IX_MovimentosFinanceiros_ConciliacaoId",
                table: "MovimentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "ConciliacaoId",
                table: "MovimentosFinanceiros");
        }
    }
}
