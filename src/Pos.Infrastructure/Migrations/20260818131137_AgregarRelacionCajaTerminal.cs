using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRelacionCajaTerminal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdCajaAsignada",
                table: "TerminalesTarjeta",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TerminalesTarjeta_IdSucursal_IdCajaAsignada",
                table: "TerminalesTarjeta",
                columns: new[] { "IdSucursal", "IdCajaAsignada" });

            migrationBuilder.AddForeignKey(
                name: "FK_TerminalesTarjeta_Cajas_IdSucursal_IdCajaAsignada",
                table: "TerminalesTarjeta",
                columns: new[] { "IdSucursal", "IdCajaAsignada" },
                principalTable: "Cajas",
                principalColumns: new[] { "IdSucursal", "IdCaja" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TerminalesTarjeta_Cajas_IdSucursal_IdCajaAsignada",
                table: "TerminalesTarjeta");

            migrationBuilder.DropIndex(
                name: "IX_TerminalesTarjeta_IdSucursal_IdCajaAsignada",
                table: "TerminalesTarjeta");

            migrationBuilder.DropColumn(
                name: "IdCajaAsignada",
                table: "TerminalesTarjeta");
        }
    }
}
