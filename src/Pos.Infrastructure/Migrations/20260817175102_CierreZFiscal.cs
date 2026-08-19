using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CierreZFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CierresZFiscal",
                columns: table => new
                {
                    IdCierreZFiscal = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdCaja = table.Column<int>(type: "int", nullable: false),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    FechaHoraUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Ok = table.Column<bool>(type: "bit", nullable: false),
                    NumeroFiscal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Referencia = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CierresZFiscal", x => x.IdCierreZFiscal);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CierresZFiscal_IdSucursal_IdCaja_FechaHoraUtc",
                table: "CierresZFiscal",
                columns: new[] { "IdSucursal", "IdCaja", "FechaHoraUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CierresZFiscal");
        }
    }
}
