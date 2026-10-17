using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class CaixaOffline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdLocal",
                table: "Vendas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Offline",
                table: "Vendas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PrecoDivergente",
                table: "Vendas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "IdLocal",
                table: "AutorizacoesGerente",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SemConferencia",
                table: "AutorizacoesGerente",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_EmpresaId_IdLocal",
                table: "Vendas",
                columns: new[] { "EmpresaId", "IdLocal" },
                unique: true,
                filter: "\"IdLocal\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacoesGerente_EmpresaId_IdLocal",
                table: "AutorizacoesGerente",
                columns: new[] { "EmpresaId", "IdLocal" },
                unique: true,
                filter: "\"IdLocal\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_EmpresaId_IdLocal",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_AutorizacoesGerente_EmpresaId_IdLocal",
                table: "AutorizacoesGerente");

            migrationBuilder.DropColumn(
                name: "IdLocal",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "Offline",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "PrecoDivergente",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "IdLocal",
                table: "AutorizacoesGerente");

            migrationBuilder.DropColumn(
                name: "SemConferencia",
                table: "AutorizacoesGerente");
        }
    }
}
