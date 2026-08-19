using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotasCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "IdDetalleOrigen",
                table: "DetallesComprobantes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdComprobanteOrigen",
                table: "CabecerasComprobantes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoAnulacion",
                table: "CabecerasComprobantes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdDetalleOrigen",
                table: "DetallesComprobantes");

            migrationBuilder.DropColumn(
                name: "IdComprobanteOrigen",
                table: "CabecerasComprobantes");

            migrationBuilder.DropColumn(
                name: "MotivoAnulacion",
                table: "CabecerasComprobantes");
        }
    }
}
