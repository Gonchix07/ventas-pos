using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarIdentificadorEquipoAPuestoCaja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentificadorEquipo",
                table: "PuestosCaja",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PuestosCaja_IdentificadorEquipo",
                table: "PuestosCaja",
                column: "IdentificadorEquipo",
                unique: true,
                filter: "[IdentificadorEquipo] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PuestosCaja_IdentificadorEquipo",
                table: "PuestosCaja");

            migrationBuilder.DropColumn(
                name: "IdentificadorEquipo",
                table: "PuestosCaja");
        }
    }
}
