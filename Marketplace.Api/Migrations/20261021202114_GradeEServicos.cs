using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class GradeEServicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cor",
                table: "Produtos",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModeloId",
                table: "Produtos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Servico",
                table: "Produtos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Tamanho",
                table: "Produtos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Modelos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, collation: "pt-BR-x-icu"),
                    Referencia = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Marca = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CategoriaId = table.Column<int>(type: "integer", nullable: false),
                    FornecedorId = table.Column<int>(type: "integer", nullable: true),
                    Tamanhos = table.Column<string[]>(type: "text[]", nullable: false),
                    Cores = table.Column<string[]>(type: "text[]", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modelos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Modelos_Categorias_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "Categorias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Modelos_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Modelos_Fornecedores_FornecedorId",
                        column: x => x.FornecedorId,
                        principalTable: "Fornecedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_EmpresaId_ModeloId_Tamanho_Cor",
                table: "Produtos",
                columns: new[] { "EmpresaId", "ModeloId", "Tamanho", "Cor" },
                unique: true,
                filter: "\"ModeloId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_ModeloId",
                table: "Produtos",
                column: "ModeloId");

            migrationBuilder.CreateIndex(
                name: "IX_Modelos_CategoriaId",
                table: "Modelos",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_Modelos_EmpresaId_Nome",
                table: "Modelos",
                columns: new[] { "EmpresaId", "Nome" });

            migrationBuilder.CreateIndex(
                name: "IX_Modelos_FornecedorId",
                table: "Modelos",
                column: "FornecedorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Produtos_Modelos_ModeloId",
                table: "Produtos",
                column: "ModeloId",
                principalTable: "Modelos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Produtos_Modelos_ModeloId",
                table: "Produtos");

            migrationBuilder.DropTable(
                name: "Modelos");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_EmpresaId_ModeloId_Tamanho_Cor",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_ModeloId",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Cor",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "ModeloId",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Servico",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Tamanho",
                table: "Produtos");
        }
    }
}
