using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CierreAdministrativoDeLote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdMotivoCierre",
                table: "LotesCaja",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdUsuarioCierre",
                table: "LotesCaja",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservacionCierre",
                table: "LotesCaja",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdMotivoCierre",
                table: "LotesCaja");

            migrationBuilder.DropColumn(
                name: "IdUsuarioCierre",
                table: "LotesCaja");

            migrationBuilder.DropColumn(
                name: "ObservacionCierre",
                table: "LotesCaja");
        }
    }
}
