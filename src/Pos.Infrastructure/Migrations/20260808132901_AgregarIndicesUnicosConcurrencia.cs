using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarIndicesUnicosConcurrencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "NumeroCompleto",
                table: "CabecerasComprobantes",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LotesCaja_UnAbiertoPorCaja",
                table: "LotesCaja",
                columns: new[] { "IdSucursal", "IdCaja" },
                unique: true,
                filter: "[Estado] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CabecerasComprobantes_IdSucursal_NumeroCompleto",
                table: "CabecerasComprobantes",
                columns: new[] { "IdSucursal", "NumeroCompleto" },
                unique: true,
                filter: "[NumeroCompleto] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LotesCaja_UnAbiertoPorCaja",
                table: "LotesCaja");

            migrationBuilder.DropIndex(
                name: "IX_CabecerasComprobantes_IdSucursal_NumeroCompleto",
                table: "CabecerasComprobantes");

            migrationBuilder.AlterColumn<string>(
                name: "NumeroCompleto",
                table: "CabecerasComprobantes",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
