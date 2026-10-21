using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrdensDeServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrdensServico",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Cliente = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Raquete = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Corda = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Tensao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Servico = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Paga = table.Column<bool>(type: "boolean", nullable: false),
                    VendaId = table.Column<int>(type: "integer", nullable: true),
                    Observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Prazo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProntaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EntregueEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CriadaPorId = table.Column<string>(type: "text", nullable: true),
                    Automatica = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdensServico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrdensServico_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrdensServico_EmpresaId_Numero",
                table: "OrdensServico",
                columns: new[] { "EmpresaId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdensServico_EmpresaId_Status_Prazo",
                table: "OrdensServico",
                columns: new[] { "EmpresaId", "Status", "Prazo" });

            migrationBuilder.CreateIndex(
                name: "IX_OrdensServico_EmpresaId_Telefone",
                table: "OrdensServico",
                columns: new[] { "EmpresaId", "Telefone" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrdensServico");
        }
    }
}
