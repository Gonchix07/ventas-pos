using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndiceComprobantePorTipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CabecerasComprobantes_IdSucursal_NumeroCompleto",
                table: "CabecerasComprobantes");

            migrationBuilder.CreateIndex(
                name: "IX_CabecerasComprobantes_IdSucursal_IdTipoComprobante_NumeroCompleto",
                table: "CabecerasComprobantes",
                columns: new[] { "IdSucursal", "IdTipoComprobante", "NumeroCompleto" },
                unique: true,
                filter: "[NumeroCompleto] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CabecerasComprobantes_IdSucursal_IdTipoComprobante_NumeroCompleto",
                table: "CabecerasComprobantes");

            migrationBuilder.CreateIndex(
                name: "IX_CabecerasComprobantes_IdSucursal_NumeroCompleto",
                table: "CabecerasComprobantes",
                columns: new[] { "IdSucursal", "NumeroCompleto" },
                unique: true,
                filter: "[NumeroCompleto] IS NOT NULL");
        }
    }
}
