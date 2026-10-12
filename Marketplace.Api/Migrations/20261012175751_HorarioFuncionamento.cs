using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Api.Migrations
{
    /// <inheritdoc />
    public partial class HorarioFuncionamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Horario",
                table: "Empresas",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"dias\":[{\"aberto\":false,\"abre\":\"08:00:00\",\"fecha\":\"19:00:00\"},{\"aberto\":true,\"abre\":\"08:00:00\",\"fecha\":\"19:00:00\"},{\"aberto\":true,\"abre\":\"08:00:00\",\"fecha\":\"19:00:00\"},{\"aberto\":true,\"abre\":\"08:00:00\",\"fecha\":\"19:00:00\"},{\"aberto\":true,\"abre\":\"08:00:00\",\"fecha\":\"19:00:00\"},{\"aberto\":true,\"abre\":\"08:00:00\",\"fecha\":\"19:00:00\"},{\"aberto\":true,\"abre\":\"08:00:00\",\"fecha\":\"19:00:00\"}],\"feriados\":\"Fechado\",\"feriadosLocais\":[]}");

            // Lojas que já existem: o horário de sempre (seg–sáb 8h–19h; domingo e feriado fechado). As de Porto
            // Alegre ganham os feriados da cidade/estado que o simulador já respeitava (Navegantes e Farroupilha).
            migrationBuilder.Sql("""
                UPDATE "Empresas"
                SET "Horario" = jsonb_set("Horario", '{feriadosLocais}', '[{"dia":2,"mes":2,"nome":"Nossa Senhora dos Navegantes"},{"dia":20,"mes":9,"nome":"Revolução Farroupilha"}]'::jsonb)
                WHERE "Cidade" = 'Porto Alegre';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Horario",
                table: "Empresas");
        }
    }
}
