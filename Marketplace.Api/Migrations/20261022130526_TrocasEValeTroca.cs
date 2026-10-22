using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrocasEValeTroca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ValeTrocaId",
                table: "PagamentosVenda",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrazoTrocaDias",
                table: "Empresas",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "ValidadeValeDias",
                table: "Empresas",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.CreateTable(
                name: "ValesTroca",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Saldo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Validade = table.Column<DateOnly>(type: "date", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Cliente = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValesTroca", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValesTroca_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Trocas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    VendaId = table.Column<int>(type: "integer", nullable: false),
                    SessaoCaixaId = table.Column<int>(type: "integer", nullable: false),
                    UsuarioId = table.Column<string>(type: "text", nullable: false),
                    DataHora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Observacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ForaDoPrazo = table.Column<bool>(type: "boolean", nullable: false),
                    ValeTrocaId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trocas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trocas_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trocas_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trocas_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trocas_ValesTroca_ValeTrocaId",
                        column: x => x.ValeTrocaId,
                        principalTable: "ValesTroca",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trocas_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItensTroca",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    TrocaId = table.Column<int>(type: "integer", nullable: false),
                    ItemVendaId = table.Column<int>(type: "integer", nullable: false),
                    ProdutoId = table.Column<int>(type: "integer", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Quantidade = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CustoUnitario = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensTroca", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensTroca_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensTroca_ItensVenda_ItemVendaId",
                        column: x => x.ItemVendaId,
                        principalTable: "ItensVenda",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensTroca_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensTroca_Trocas_TrocaId",
                        column: x => x.TrocaId,
                        principalTable: "Trocas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosVenda_ValeTrocaId",
                table: "PagamentosVenda",
                column: "ValeTrocaId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensTroca_EmpresaId",
                table: "ItensTroca",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensTroca_ItemVendaId",
                table: "ItensTroca",
                column: "ItemVendaId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensTroca_ProdutoId",
                table: "ItensTroca",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensTroca_TrocaId",
                table: "ItensTroca",
                column: "TrocaId");

            migrationBuilder.CreateIndex(
                name: "IX_Trocas_EmpresaId_DataHora",
                table: "Trocas",
                columns: new[] { "EmpresaId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Trocas_SessaoCaixaId",
                table: "Trocas",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_Trocas_UsuarioId",
                table: "Trocas",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Trocas_ValeTrocaId",
                table: "Trocas",
                column: "ValeTrocaId");

            migrationBuilder.CreateIndex(
                name: "IX_Trocas_VendaId",
                table: "Trocas",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_ValesTroca_EmpresaId_Codigo",
                table: "ValesTroca",
                columns: new[] { "EmpresaId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValesTroca_EmpresaId_Validade",
                table: "ValesTroca",
                columns: new[] { "EmpresaId", "Validade" });

            migrationBuilder.AddForeignKey(
                name: "FK_PagamentosVenda_ValesTroca_ValeTrocaId",
                table: "PagamentosVenda",
                column: "ValeTrocaId",
                principalTable: "ValesTroca",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PagamentosVenda_ValesTroca_ValeTrocaId",
                table: "PagamentosVenda");

            migrationBuilder.DropTable(
                name: "ItensTroca");

            migrationBuilder.DropTable(
                name: "Trocas");

            migrationBuilder.DropTable(
                name: "ValesTroca");

            migrationBuilder.DropIndex(
                name: "IX_PagamentosVenda_ValeTrocaId",
                table: "PagamentosVenda");

            migrationBuilder.DropColumn(
                name: "ValeTrocaId",
                table: "PagamentosVenda");

            migrationBuilder.DropColumn(
                name: "PrazoTrocaDias",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "ValidadeValeDias",
                table: "Empresas");
        }
    }
}
