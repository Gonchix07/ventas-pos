using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorregirIndiceFamiliaCodigoErp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Familias_CodigoErp",
                table: "Familias");

            migrationBuilder.DropIndex(
                name: "IX_Familias_IdSector",
                table: "Familias");

            migrationBuilder.CreateIndex(
                name: "IX_Familias_IdSector_CodigoErp",
                table: "Familias",
                columns: new[] { "IdSector", "CodigoErp" },
                unique: true,
                filter: "[IdSector] IS NOT NULL AND [CodigoErp] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Familias_IdSector_CodigoErp",
                table: "Familias");

            migrationBuilder.CreateIndex(
                name: "IX_Familias_CodigoErp",
                table: "Familias",
                column: "CodigoErp",
                unique: true,
                filter: "[CodigoErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Familias_IdSector",
                table: "Familias",
                column: "IdSector");
        }
    }
}
