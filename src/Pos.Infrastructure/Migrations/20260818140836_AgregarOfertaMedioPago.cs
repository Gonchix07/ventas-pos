using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarOfertaMedioPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfertasMedioPago",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdOfertaMedioPago = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdMedioPago = table.Column<int>(type: "int", nullable: false),
                    IdPlanCuota = table.Column<int>(type: "int", nullable: true),
                    Porcentaje = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TopeMaximo = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfertasMedioPago", x => new { x.IdSucursal, x.IdOfertaMedioPago });
                    table.ForeignKey(
                        name: "FK_OfertasMedioPago_MediosPago_IdMedioPago",
                        column: x => x.IdMedioPago,
                        principalTable: "MediosPago",
                        principalColumn: "IdMedioPago",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfertasMedioPago_PlanesCuota_IdPlanCuota",
                        column: x => x.IdPlanCuota,
                        principalTable: "PlanesCuota",
                        principalColumn: "IdPlan",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfertasMedioPago_IdMedioPago",
                table: "OfertasMedioPago",
                column: "IdMedioPago");

            migrationBuilder.CreateIndex(
                name: "IX_OfertasMedioPago_IdPlanCuota",
                table: "OfertasMedioPago",
                column: "IdPlanCuota");

            migrationBuilder.CreateIndex(
                name: "IX_OfertasMedioPago_IdSucursal_IdMedioPago_Activo",
                table: "OfertasMedioPago",
                columns: new[] { "IdSucursal", "IdMedioPago", "Activo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfertasMedioPago");
        }
    }
}
