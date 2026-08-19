using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LoteAbiertoPorCajaCajeroYDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LotesCaja_UnAbiertoPorCajaYCajero",
                table: "LotesCaja");

            migrationBuilder.AddColumn<DateOnly>(
                name: "DiaApertura",
                table: "LotesCaja",
                type: "date",
                nullable: false,
                computedColumnSql: "CONVERT(date, [FechaApertura])",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_LotesCaja_UnAbiertoPorCajaCajeroYDia",
                table: "LotesCaja",
                columns: new[] { "IdSucursal", "IdCaja", "IdUsuarioApertura", "DiaApertura" },
                unique: true,
                filter: "[Estado] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LotesCaja_UnAbiertoPorCajaCajeroYDia",
                table: "LotesCaja");

            migrationBuilder.DropColumn(
                name: "DiaApertura",
                table: "LotesCaja");

            migrationBuilder.CreateIndex(
                name: "IX_LotesCaja_UnAbiertoPorCajaYCajero",
                table: "LotesCaja",
                columns: new[] { "IdSucursal", "IdCaja", "IdUsuarioApertura" },
                unique: true,
                filter: "[Estado] = 1");
        }
    }
}
