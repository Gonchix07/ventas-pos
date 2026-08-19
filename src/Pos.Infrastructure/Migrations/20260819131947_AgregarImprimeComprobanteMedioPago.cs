using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarImprimeComprobanteMedioPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default true: para los medios ya cargados, que no impriman comprobante hoy no es lo
            // mismo que "no corresponde" — se preserva el comportamiento implícito actual (se asume
            // que si nadie lo definió, imprime) hasta que se revisen uno por uno desde el ABM.
            migrationBuilder.AddColumn<bool>(
                name: "ImprimeComprobante",
                table: "MediosPago",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImprimeComprobante",
                table: "MediosPago");
        }
    }
}
