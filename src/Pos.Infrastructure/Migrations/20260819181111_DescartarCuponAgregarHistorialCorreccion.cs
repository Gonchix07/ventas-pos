using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DescartarCuponAgregarHistorialCorreccion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cupones");

            migrationBuilder.CreateTable(
                name: "CorreccionesCupon",
                columns: table => new
                {
                    IdCorreccionCupon = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdMovPagos = table.Column<long>(type: "bigint", nullable: false),
                    NumeroCuponAnterior = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NumeroLoteAnterior = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IdPlanCuotaAnterior = table.Column<int>(type: "int", nullable: true),
                    NumeroCuponNuevo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NumeroLoteNuevo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IdPlanCuotaNuevo = table.Column<int>(type: "int", nullable: true),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorreccionesCupon", x => x.IdCorreccionCupon);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorreccionesCupon");

            migrationBuilder.CreateTable(
                name: "Cupones",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdCupon = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdComprobante = table.Column<int>(type: "int", nullable: true),
                    IdMedioPago = table.Column<int>(type: "int", nullable: false),
                    NroCupon = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NroLote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cupones", x => new { x.IdSucursal, x.IdCupon });
                });
        }
    }
}
