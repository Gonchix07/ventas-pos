using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FamiliaPorSector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdSector",
                table: "Familias",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Familias_IdSector",
                table: "Familias",
                column: "IdSector");

            migrationBuilder.AddForeignKey(
                name: "FK_Familias_Sectores_IdSector",
                table: "Familias",
                column: "IdSector",
                principalTable: "Sectores",
                principalColumn: "IdSector",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Familias_Sectores_IdSector",
                table: "Familias");

            migrationBuilder.DropIndex(
                name: "IX_Familias_IdSector",
                table: "Familias");

            migrationBuilder.DropColumn(
                name: "IdSector",
                table: "Familias");
        }
    }
}
