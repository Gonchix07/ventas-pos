using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanesCuotaMedioPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CantidadCuotas",
                table: "MovimientosPagos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdPlanCuota",
                table: "MovimientosPagos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanesCuota",
                columns: table => new
                {
                    IdPlan = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdMedioPago = table.Column<int>(type: "int", nullable: false),
                    Denominacion = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CantidadCuotas = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanesCuota", x => x.IdPlan);
                    table.ForeignKey(
                        name: "FK_PlanesCuota_MediosPago_IdMedioPago",
                        column: x => x.IdMedioPago,
                        principalTable: "MediosPago",
                        principalColumn: "IdMedioPago",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanesCuota_IdMedioPago",
                table: "PlanesCuota",
                column: "IdMedioPago");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanesCuota");

            migrationBuilder.DropColumn(
                name: "CantidadCuotas",
                table: "MovimientosPagos");

            migrationBuilder.DropColumn(
                name: "IdPlanCuota",
                table: "MovimientosPagos");
        }
    }
}
