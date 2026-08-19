using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LoteCajaPorCajero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LotesCaja_UnAbiertoPorCaja",
                table: "LotesCaja");

            migrationBuilder.CreateIndex(
                name: "IX_LotesCaja_UnAbiertoPorCajaYCajero",
                table: "LotesCaja",
                columns: new[] { "IdSucursal", "IdCaja", "IdUsuarioApertura" },
                unique: true,
                filter: "[Estado] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LotesCaja_UnAbiertoPorCajaYCajero",
                table: "LotesCaja");

            migrationBuilder.CreateIndex(
                name: "IX_LotesCaja_UnAbiertoPorCaja",
                table: "LotesCaja",
                columns: new[] { "IdSucursal", "IdCaja" },
                unique: true,
                filter: "[Estado] = 1");
        }
    }
}
