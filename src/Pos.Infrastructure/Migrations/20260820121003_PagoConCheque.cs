using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PagoConCheque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdBanco",
                table: "MovimientosPagos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroCheque",
                table: "MovimientosPagos",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservacionesCheque",
                table: "MovimientosPagos",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Bancos",
                columns: table => new
                {
                    IdBanco = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bancos", x => x.IdBanco);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosPagos_IdBanco",
                table: "MovimientosPagos",
                column: "IdBanco");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimientosPagos_Bancos_IdBanco",
                table: "MovimientosPagos",
                column: "IdBanco",
                principalTable: "Bancos",
                principalColumn: "IdBanco",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovimientosPagos_Bancos_IdBanco",
                table: "MovimientosPagos");

            migrationBuilder.DropTable(
                name: "Bancos");

            migrationBuilder.DropIndex(
                name: "IX_MovimientosPagos_IdBanco",
                table: "MovimientosPagos");

            migrationBuilder.DropColumn(
                name: "IdBanco",
                table: "MovimientosPagos");

            migrationBuilder.DropColumn(
                name: "NumeroCheque",
                table: "MovimientosPagos");

            migrationBuilder.DropColumn(
                name: "ObservacionesCheque",
                table: "MovimientosPagos");
        }
    }
}
