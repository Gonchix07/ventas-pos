using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MedioPredeterminadoYCuponTarjeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NumeroCupon",
                table: "MovimientosPagos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroLote",
                table: "MovimientosPagos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EsPredeterminado",
                table: "MediosPago",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Arranca marcando Efectivo (Fuente = 1) como predeterminado: es el que la caja venía
            // proponiendo de hecho —era el primero de la lista— y sin esto ningún medio quedaría
            // marcado y el cobro abriría con uno arbitrario.
            migrationBuilder.Sql(@"
                UPDATE TOP (1) m SET m.EsPredeterminado = 1
                FROM MediosPago m
                JOIN TiposPago t ON t.IdTipoPago = m.IdTipoPago
                WHERE t.Fuente = 1 AND m.Activo = 1
                  AND NOT EXISTS (SELECT 1 FROM MediosPago x WHERE x.EsPredeterminado = 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumeroCupon",
                table: "MovimientosPagos");

            migrationBuilder.DropColumn(
                name: "NumeroLote",
                table: "MovimientosPagos");

            migrationBuilder.DropColumn(
                name: "EsPredeterminado",
                table: "MediosPago");
        }
    }
}
