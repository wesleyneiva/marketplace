using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class Fiado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClienteId",
                table: "Vendas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, collation: "pt-BR-x-icu"),
                    Apelido = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    Endereco = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LimiteCredito = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DiaPagamento = table.Column<int>(type: "integer", nullable: false),
                    Bloqueado = table.Column<bool>(type: "boolean", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                    table.CheckConstraint("CK_Clientes_DiaPagamento", "\"DiaPagamento\" BETWEEN 1 AND 28");
                    table.ForeignKey(
                        name: "FK_Clientes_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LancamentosFiado",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    ClienteId = table.Column<int>(type: "integer", nullable: false),
                    DataHora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: true),
                    VendaId = table.Column<int>(type: "integer", nullable: true),
                    SessaoCaixaId = table.Column<int>(type: "integer", nullable: true),
                    Forma = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    MovimentoFinanceiroId = table.Column<int>(type: "integer", nullable: true),
                    Observacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UsuarioId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LancamentosFiado", x => x.Id);
                    table.CheckConstraint("CK_LancamentosFiado_ValorPositivo", "\"Valor\" > 0");
                    table.ForeignKey(
                        name: "FK_LancamentosFiado_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LancamentosFiado_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LancamentosFiado_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LancamentosFiado_MovimentosFinanceiros_MovimentoFinanceiroId",
                        column: x => x.MovimentoFinanceiroId,
                        principalTable: "MovimentosFinanceiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LancamentosFiado_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LancamentosFiado_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_ClienteId",
                table: "Vendas",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_EmpresaId_Cpf",
                table: "Clientes",
                columns: new[] { "EmpresaId", "Cpf" },
                unique: true,
                filter: "\"Cpf\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_EmpresaId_Nome",
                table: "Clientes",
                columns: new[] { "EmpresaId", "Nome" });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFiado_ClienteId_DataHora",
                table: "LancamentosFiado",
                columns: new[] { "ClienteId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFiado_EmpresaId",
                table: "LancamentosFiado",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFiado_MovimentoFinanceiroId",
                table: "LancamentosFiado",
                column: "MovimentoFinanceiroId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFiado_SessaoCaixaId",
                table: "LancamentosFiado",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFiado_UsuarioId",
                table: "LancamentosFiado",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFiado_VendaId",
                table: "LancamentosFiado",
                column: "VendaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Vendas_Clientes_ClienteId",
                table: "Vendas",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vendas_Clientes_ClienteId",
                table: "Vendas");

            migrationBuilder.DropTable(
                name: "LancamentosFiado");

            migrationBuilder.DropTable(
                name: "Clientes");

            migrationBuilder.DropIndex(
                name: "IX_Vendas_ClienteId",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "ClienteId",
                table: "Vendas");
        }
    }
}
