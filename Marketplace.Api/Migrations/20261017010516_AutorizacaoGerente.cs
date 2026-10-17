using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class AutorizacaoGerente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DevolvidaEm",
                table: "CobrancasPix",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AutorizacoesGerente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    DataHora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Acao = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    SessaoCaixaId = table.Column<int>(type: "integer", nullable: true),
                    OperadorId = table.Column<string>(type: "text", nullable: false),
                    AutorizadoPorId = table.Column<string>(type: "text", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    VendaId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutorizacoesGerente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutorizacoesGerente_AspNetUsers_AutorizadoPorId",
                        column: x => x.AutorizadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutorizacoesGerente_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutorizacoesGerente_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutorizacoesGerente_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutorizacoesGerente_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacoesGerente_AutorizadoPorId",
                table: "AutorizacoesGerente",
                column: "AutorizadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacoesGerente_EmpresaId_DataHora",
                table: "AutorizacoesGerente",
                columns: new[] { "EmpresaId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacoesGerente_OperadorId",
                table: "AutorizacoesGerente",
                column: "OperadorId");

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacoesGerente_SessaoCaixaId",
                table: "AutorizacoesGerente",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacoesGerente_VendaId",
                table: "AutorizacoesGerente",
                column: "VendaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutorizacoesGerente");

            migrationBuilder.DropColumn(
                name: "DevolvidaEm",
                table: "CobrancasPix");
        }
    }
}
