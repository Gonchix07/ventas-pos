using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FkCajaPuestoAsignado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Cajas_IdSucursal_IdPuestoAsignado",
                table: "Cajas",
                columns: new[] { "IdSucursal", "IdPuestoAsignado" });

            migrationBuilder.AddForeignKey(
                name: "FK_Cajas_PuestosCaja_IdSucursal_IdPuestoAsignado",
                table: "Cajas",
                columns: new[] { "IdSucursal", "IdPuestoAsignado" },
                principalTable: "PuestosCaja",
                principalColumns: new[] { "IdSucursal", "IdPuestoAsignado" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cajas_PuestosCaja_IdSucursal_IdPuestoAsignado",
                table: "Cajas");

            migrationBuilder.DropIndex(
                name: "IX_Cajas_IdSucursal_IdPuestoAsignado",
                table: "Cajas");
        }
    }
}
