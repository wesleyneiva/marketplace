using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class PrecoAlteradoEm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PrecoAlteradoEm",
                table: "Produtos",
                type: "timestamp with time zone",
                nullable: true);

            // Produtos que já existiam: considera o preço "alterado" quando foram cadastrados.
            migrationBuilder.Sql("UPDATE \"Produtos\" SET \"PrecoAlteradoEm\" = \"CriadoEm\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrecoAlteradoEm",
                table: "Produtos");
        }
    }
}
