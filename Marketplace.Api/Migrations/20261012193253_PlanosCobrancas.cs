using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlanosCobrancas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CobrarDesde",
                table: "Empresas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Cortesia",
                table: "Empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DiaVencimento",
                table: "Empresas",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "MotivoSuspensao",
                table: "Empresas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanoId",
                table: "Empresas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecoCombinado",
                table: "Empresas",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspensaEm",
                table: "Empresas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TesteAte",
                table: "Empresas",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConfiguracoesPlataforma",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DiasGerarAntes = table.Column<int>(type: "integer", nullable: false),
                    BloqueioAutomatico = table.Column<bool>(type: "boolean", nullable: false),
                    DiasParaBloquear = table.Column<int>(type: "integer", nullable: false),
                    DiasAvisoAntes = table.Column<int>(type: "integer", nullable: false),
                    ChavePix = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NomeRecebedor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContatoCobranca = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesPlataforma", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Faturas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClienteId = table.Column<int>(type: "integer", nullable: false),
                    Competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PagaEm = table.Column<DateOnly>(type: "date", nullable: true),
                    ValorPago = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    FormaPagamento = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faturas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Faturas_Empresas_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Planos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PrecoMensal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CaixasIncluidos = table.Column<int>(type: "integer", nullable: false),
                    PrecoCaixaExtra = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Planos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_PlanoId",
                table: "Empresas",
                column: "PlanoId");

            migrationBuilder.CreateIndex(
                name: "IX_Faturas_ClienteId_Competencia",
                table: "Faturas",
                columns: new[] { "ClienteId", "Competencia" },
                unique: true,
                filter: "\"Status\" <> 'Cancelada'");

            migrationBuilder.CreateIndex(
                name: "IX_Faturas_Status_Vencimento",
                table: "Faturas",
                columns: new[] { "Status", "Vencimento" });

            migrationBuilder.CreateIndex(
                name: "IX_Planos_Nome",
                table: "Planos",
                column: "Nome",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Empresas_Planos_PlanoId",
                table: "Empresas",
                column: "PlanoId",
                principalTable: "Planos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Plano inicial (preços decididos em 12/10/2026; editáveis na tela Planos) e as regras de cobrança.
            migrationBuilder.Sql("""
                INSERT INTO "Planos" ("Nome", "Descricao", "PrecoMensal", "CaixasIncluidos", "PrecoCaixaExtra", "Ativo", "CriadoEm")
                VALUES ('Básico', 'Sistema completo com 1 caixa; caixa extra à parte.', 149, 1, 49, true, now());
                INSERT INTO "ConfiguracoesPlataforma" ("Id", "DiasGerarAntes", "BloqueioAutomatico", "DiasParaBloquear", "DiasAvisoAntes")
                VALUES (1, 10, true, 10, 5);
                -- Clientes que já existem são todos de teste ou demonstração: cortesia (sem cobrança) até o dono mudar.
                UPDATE "Empresas" SET "Cortesia" = true WHERE NOT "Plataforma";
                UPDATE "Empresas" SET "MotivoSuspensao" = 'Manual' WHERE NOT "Ativa";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Empresas_Planos_PlanoId",
                table: "Empresas");

            migrationBuilder.DropTable(
                name: "ConfiguracoesPlataforma");

            migrationBuilder.DropTable(
                name: "Faturas");

            migrationBuilder.DropTable(
                name: "Planos");

            migrationBuilder.DropIndex(
                name: "IX_Empresas_PlanoId",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "CobrarDesde",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Cortesia",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "DiaVencimento",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "MotivoSuspensao",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PlanoId",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PrecoCombinado",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "SuspensaEm",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "TesteAte",
                table: "Empresas");
        }
    }
}
