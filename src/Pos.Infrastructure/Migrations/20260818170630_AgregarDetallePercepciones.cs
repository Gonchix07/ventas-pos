using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarDetallePercepciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PercepcionIibb",
                table: "CabecerasComprobantes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PercepcionIva105",
                table: "CabecerasComprobantes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PercepcionIva21",
                table: "CabecerasComprobantes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PercepcionIibb",
                table: "CabecerasComprobantes");

            migrationBuilder.DropColumn(
                name: "PercepcionIva105",
                table: "CabecerasComprobantes");

            migrationBuilder.DropColumn(
                name: "PercepcionIva21",
                table: "CabecerasComprobantes");
        }
    }
}
