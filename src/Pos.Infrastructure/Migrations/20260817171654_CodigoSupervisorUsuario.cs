using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CodigoSupervisorUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoSupervisor",
                table: "Usuarios",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_CodigoSupervisor",
                table: "Usuarios",
                column: "CodigoSupervisor",
                unique: true,
                filter: "[CodigoSupervisor] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Usuarios_CodigoSupervisor",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "CodigoSupervisor",
                table: "Usuarios");
        }
    }
}
