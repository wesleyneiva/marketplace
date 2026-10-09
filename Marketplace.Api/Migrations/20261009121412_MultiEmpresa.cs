using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class MultiEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_DataHora",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_Vendas_Origem_DataHora",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_SessoesCaixa_UmaAbertaPorCaixa",
                table: "SessoesCaixa");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_CodigoBarras",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_Nome",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_Movimentacoes_DataHora",
                table: "Movimentacoes");

            migrationBuilder.DropIndex(
                name: "IX_Fornecedores_Nome",
                table: "Fornecedores");

            migrationBuilder.DropIndex(
                name: "IX_Categorias_Nome",
                table: "Categorias");

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Vendas",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "SessoesCaixa",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Produtos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "PedidosCompra",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "PagamentosVenda",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "MovimentosCaixa",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Movimentacoes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Lotes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "ItensVenda",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "ItensPedidoCompra",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Fornecedores",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Categorias",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "SomenteLeitura",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Empresas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Subdominio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LimiteCaixas = table.Column<int>(type: "integer", nullable: false),
                    Demonstracao = table.Column<bool>(type: "boolean", nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empresas", x => x.Id);
                });

            // A empresa 1: o Marketplace de demonstração. Todos os dados que já existiam são dela.
            migrationBuilder.Sql("""
                INSERT INTO "Empresas" ("Id", "Nome", "Subdominio", "LimiteCaixas", "Demonstracao", "Ativa", "CriadoEm")
                OVERRIDING SYSTEM VALUE
                VALUES (1, 'Marketplace', 'demo', 10, true, true, now());
                SELECT setval(pg_get_serial_sequence('"Empresas"', 'Id'), 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_EmpresaId_DataHora",
                table: "Vendas",
                columns: new[] { "EmpresaId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_EmpresaId_Origem_DataHora",
                table: "Vendas",
                columns: new[] { "EmpresaId", "Origem", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_UmaAbertaPorCaixa",
                table: "SessoesCaixa",
                columns: new[] { "EmpresaId", "NumeroCaixa" },
                unique: true,
                filter: "\"Status\" = 'Aberta'");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_EmpresaId_CodigoBarras",
                table: "Produtos",
                columns: new[] { "EmpresaId", "CodigoBarras" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_EmpresaId_Nome",
                table: "Produtos",
                columns: new[] { "EmpresaId", "Nome" });

            migrationBuilder.CreateIndex(
                name: "IX_PedidosCompra_EmpresaId",
                table: "PedidosCompra",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosVenda_EmpresaId",
                table: "PagamentosVenda",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_EmpresaId",
                table: "MovimentosCaixa",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Movimentacoes_EmpresaId_DataHora",
                table: "Movimentacoes",
                columns: new[] { "EmpresaId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Lotes_EmpresaId",
                table: "Lotes",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVenda_EmpresaId",
                table: "ItensVenda",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensPedidoCompra_EmpresaId",
                table: "ItensPedidoCompra",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Fornecedores_EmpresaId_Nome",
                table: "Fornecedores",
                columns: new[] { "EmpresaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_EmpresaId_Nome",
                table: "Categorias",
                columns: new[] { "EmpresaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_EmpresaId",
                table: "AspNetUsers",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_Subdominio",
                table: "Empresas",
                column: "Subdominio",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Empresas_EmpresaId",
                table: "AspNetUsers",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Categorias_Empresas_EmpresaId",
                table: "Categorias",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Fornecedores_Empresas_EmpresaId",
                table: "Fornecedores",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ItensPedidoCompra_Empresas_EmpresaId",
                table: "ItensPedidoCompra",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ItensVenda_Empresas_EmpresaId",
                table: "ItensVenda",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lotes_Empresas_EmpresaId",
                table: "Lotes",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movimentacoes_Empresas_EmpresaId",
                table: "Movimentacoes",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MovimentosCaixa_Empresas_EmpresaId",
                table: "MovimentosCaixa",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PagamentosVenda_Empresas_EmpresaId",
                table: "PagamentosVenda",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PedidosCompra_Empresas_EmpresaId",
                table: "PedidosCompra",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Produtos_Empresas_EmpresaId",
                table: "Produtos",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SessoesCaixa_Empresas_EmpresaId",
                table: "SessoesCaixa",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendas_Empresas_EmpresaId",
                table: "Vendas",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Daqui para frente o sistema SEMPRE informa a empresa: nada "cai" na empresa 1 sem querer.
            migrationBuilder.Sql("ALTER TABLE \"Vendas\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"SessoesCaixa\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"Produtos\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"PedidosCompra\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"PagamentosVenda\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"MovimentosCaixa\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"Movimentacoes\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"Lotes\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"ItensVenda\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"ItensPedidoCompra\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"Fornecedores\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"Categorias\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"AspNetUsers\" ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Empresas_EmpresaId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Categorias_Empresas_EmpresaId",
                table: "Categorias");

            migrationBuilder.DropForeignKey(
                name: "FK_Fornecedores_Empresas_EmpresaId",
                table: "Fornecedores");

            migrationBuilder.DropForeignKey(
                name: "FK_ItensPedidoCompra_Empresas_EmpresaId",
                table: "ItensPedidoCompra");

            migrationBuilder.DropForeignKey(
                name: "FK_ItensVenda_Empresas_EmpresaId",
                table: "ItensVenda");

            migrationBuilder.DropForeignKey(
                name: "FK_Lotes_Empresas_EmpresaId",
                table: "Lotes");

            migrationBuilder.DropForeignKey(
                name: "FK_Movimentacoes_Empresas_EmpresaId",
                table: "Movimentacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_MovimentosCaixa_Empresas_EmpresaId",
                table: "MovimentosCaixa");

            migrationBuilder.DropForeignKey(
                name: "FK_PagamentosVenda_Empresas_EmpresaId",
                table: "PagamentosVenda");

            migrationBuilder.DropForeignKey(
                name: "FK_PedidosCompra_Empresas_EmpresaId",
                table: "PedidosCompra");

            migrationBuilder.DropForeignKey(
                name: "FK_Produtos_Empresas_EmpresaId",
                table: "Produtos");

            migrationBuilder.DropForeignKey(
                name: "FK_SessoesCaixa_Empresas_EmpresaId",
                table: "SessoesCaixa");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendas_Empresas_EmpresaId",
                table: "Vendas");

            migrationBuilder.DropTable(
                name: "Empresas");

            migrationBuilder.DropIndex(
                name: "IX_Vendas_EmpresaId_DataHora",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_Vendas_EmpresaId_Origem_DataHora",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_SessoesCaixa_UmaAbertaPorCaixa",
                table: "SessoesCaixa");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_EmpresaId_CodigoBarras",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_EmpresaId_Nome",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_PedidosCompra_EmpresaId",
                table: "PedidosCompra");

            migrationBuilder.DropIndex(
                name: "IX_PagamentosVenda_EmpresaId",
                table: "PagamentosVenda");

            migrationBuilder.DropIndex(
                name: "IX_MovimentosCaixa_EmpresaId",
                table: "MovimentosCaixa");

            migrationBuilder.DropIndex(
                name: "IX_Movimentacoes_EmpresaId_DataHora",
                table: "Movimentacoes");

            migrationBuilder.DropIndex(
                name: "IX_Lotes_EmpresaId",
                table: "Lotes");

            migrationBuilder.DropIndex(
                name: "IX_ItensVenda_EmpresaId",
                table: "ItensVenda");

            migrationBuilder.DropIndex(
                name: "IX_ItensPedidoCompra_EmpresaId",
                table: "ItensPedidoCompra");

            migrationBuilder.DropIndex(
                name: "IX_Fornecedores_EmpresaId_Nome",
                table: "Fornecedores");

            migrationBuilder.DropIndex(
                name: "IX_Categorias_EmpresaId_Nome",
                table: "Categorias");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_EmpresaId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "SessoesCaixa");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "PedidosCompra");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "PagamentosVenda");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "MovimentosCaixa");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Movimentacoes");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Lotes");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "ItensVenda");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "ItensPedidoCompra");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Fornecedores");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Categorias");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SomenteLeitura",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_DataHora",
                table: "Vendas",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_Origem_DataHora",
                table: "Vendas",
                columns: new[] { "Origem", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_UmaAbertaPorCaixa",
                table: "SessoesCaixa",
                column: "NumeroCaixa",
                unique: true,
                filter: "\"Status\" = 'Aberta'");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_CodigoBarras",
                table: "Produtos",
                column: "CodigoBarras",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_Nome",
                table: "Produtos",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_Movimentacoes_DataHora",
                table: "Movimentacoes",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_Fornecedores_Nome",
                table: "Fornecedores",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_Nome",
                table: "Categorias",
                column: "Nome",
                unique: true);
        }
    }
}
