using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarMovimientoManualTipado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TipoManual",
                table: "MovimientosCaja",
                type: "int",
                nullable: true);

            // Backfill de las filas históricas: hasta ahora Retiro/Vuelto se distinguían por un
            // prefijo de texto en Concepto (ver CierreLoteEjecutor, antes de este cambio). Se
            // clasifican acá una única vez para que el histórico no quede "sin tipo" — de acá en
            // adelante los servicios ya escriben TipoManual directamente, esto no vuelve a hacer
            // falta. TipoManual: 2 = Retiro, 3 = Vuelto (ver enum TipoMovimientoManual).
            migrationBuilder.Sql(
                "UPDATE MovimientosCaja SET TipoManual = 3 " +
                "WHERE IdComprobante IS NULL AND Concepto LIKE 'Vuelto%';");
            migrationBuilder.Sql(
                "UPDATE MovimientosCaja SET TipoManual = 2 " +
                "WHERE IdComprobante IS NULL AND TipoManual IS NULL " +
                "AND (Concepto IS NULL OR Concepto NOT LIKE 'Vuelto%');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TipoManual",
                table: "MovimientosCaja");
        }
    }
}
