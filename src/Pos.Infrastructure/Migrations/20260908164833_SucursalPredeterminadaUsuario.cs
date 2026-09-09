using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SucursalPredeterminadaUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdSucursalPredeterminada",
                table: "Usuarios",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_IdSucursalPredeterminada",
                table: "Usuarios",
                column: "IdSucursalPredeterminada");

            migrationBuilder.AddForeignKey(
                name: "FK_Usuarios_Sucursales_IdSucursalPredeterminada",
                table: "Usuarios",
                column: "IdSucursalPredeterminada",
                principalTable: "Sucursales",
                principalColumn: "IdSucursal",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_Sucursales_IdSucursalPredeterminada",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_IdSucursalPredeterminada",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "IdSucursalPredeterminada",
                table: "Usuarios");
        }
    }
}
