using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class Recebiveis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LancadaNoFinanceiroEm",
                table: "SessoesCaixa",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LiquidacaoId",
                table: "MovimentosFinanceiros",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessaoCaixaId",
                table: "MovimentosFinanceiros",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConfiguracoesRecebimento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Forma = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TaxaPercentual = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    PrazoDias = table.Column<int>(type: "integer", nullable: false),
                    DiasUteis = table.Column<bool>(type: "boolean", nullable: false),
                    ContaFinanceiraId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesRecebimento", x => x.Id);
                    table.CheckConstraint("CK_ConfiguracoesRecebimento_Valores", "\"TaxaPercentual\" >= 0 AND \"TaxaPercentual\" < 100 AND \"PrazoDias\" >= 0 AND \"PrazoDias\" <= 365");
                    table.ForeignKey(
                        name: "FK_ConfiguracoesRecebimento_ContasFinanceiras_ContaFinanceiraId",
                        column: x => x.ContaFinanceiraId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfiguracoesRecebimento_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Recebiveis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    SessaoCaixaId = table.Column<int>(type: "integer", nullable: false),
                    Forma = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DataVenda = table.Column<DateOnly>(type: "date", nullable: false),
                    Previsao = table.Column<DateOnly>(type: "date", nullable: false),
                    ValorBruto = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    TaxaPercentual = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    Taxa = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ValorLiquido = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ContaFinanceiraId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RecebidoEm = table.Column<DateOnly>(type: "date", nullable: true),
                    LiquidacaoId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recebiveis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recebiveis_ContasFinanceiras_ContaFinanceiraId",
                        column: x => x.ContaFinanceiraId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recebiveis_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recebiveis_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_LiquidacaoId",
                table: "MovimentosFinanceiros",
                column: "LiquidacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosFinanceiros_SessaoCaixaId",
                table: "MovimentosFinanceiros",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracoesRecebimento_ContaFinanceiraId",
                table: "ConfiguracoesRecebimento",
                column: "ContaFinanceiraId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracoesRecebimento_EmpresaId_Forma",
                table: "ConfiguracoesRecebimento",
                columns: new[] { "EmpresaId", "Forma" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recebiveis_ContaFinanceiraId",
                table: "Recebiveis",
                column: "ContaFinanceiraId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebiveis_EmpresaId_Status_Previsao",
                table: "Recebiveis",
                columns: new[] { "EmpresaId", "Status", "Previsao" });

            migrationBuilder.CreateIndex(
                name: "IX_Recebiveis_LiquidacaoId",
                table: "Recebiveis",
                column: "LiquidacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebiveis_SessaoCaixaId_Forma",
                table: "Recebiveis",
                columns: new[] { "SessaoCaixaId", "Forma" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MovimentosFinanceiros_SessoesCaixa_SessaoCaixaId",
                table: "MovimentosFinanceiros",
                column: "SessaoCaixaId",
                principalTable: "SessoesCaixa",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovimentosFinanceiros_SessoesCaixa_SessaoCaixaId",
                table: "MovimentosFinanceiros");

            migrationBuilder.DropTable(
                name: "ConfiguracoesRecebimento");

            migrationBuilder.DropTable(
                name: "Recebiveis");

            migrationBuilder.DropIndex(
                name: "IX_MovimentosFinanceiros_LiquidacaoId",
                table: "MovimentosFinanceiros");

            migrationBuilder.DropIndex(
                name: "IX_MovimentosFinanceiros_SessaoCaixaId",
                table: "MovimentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "LancadaNoFinanceiroEm",
                table: "SessoesCaixa");

            migrationBuilder.DropColumn(
                name: "LiquidacaoId",
                table: "MovimentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "SessaoCaixaId",
                table: "MovimentosFinanceiros");
        }
    }
}
