using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarListaPrecioEnlazadaYDiferenciales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdListaBase",
                table: "ListasPrecios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoLegacyVfp",
                table: "Lineas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiferencialesListaPrecio",
                columns: table => new
                {
                    IdDiferencial = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdListaPrecio = table.Column<int>(type: "int", nullable: false),
                    IdLinea = table.Column<int>(type: "int", nullable: true),
                    IdArticulo = table.Column<int>(type: "int", nullable: true),
                    Porcentaje = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiferencialesListaPrecio", x => x.IdDiferencial);
                    table.ForeignKey(
                        name: "FK_DiferencialesListaPrecio_Articulos_IdArticulo",
                        column: x => x.IdArticulo,
                        principalTable: "Articulos",
                        principalColumn: "IdArticulo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiferencialesListaPrecio_Lineas_IdLinea",
                        column: x => x.IdLinea,
                        principalTable: "Lineas",
                        principalColumn: "IdLinea",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiferencialesListaPrecio_ListasPrecios_IdListaPrecio",
                        column: x => x.IdListaPrecio,
                        principalTable: "ListasPrecios",
                        principalColumn: "IdListaPrecio",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListasPrecios_IdListaBase",
                table: "ListasPrecios",
                column: "IdListaBase");

            migrationBuilder.CreateIndex(
                name: "IX_DiferencialesListaPrecio_IdArticulo",
                table: "DiferencialesListaPrecio",
                column: "IdArticulo");

            migrationBuilder.CreateIndex(
                name: "IX_DiferencialesListaPrecio_IdLinea",
                table: "DiferencialesListaPrecio",
                column: "IdLinea");

            migrationBuilder.CreateIndex(
                name: "IX_DiferencialesListaPrecio_IdListaPrecio",
                table: "DiferencialesListaPrecio",
                column: "IdListaPrecio");

            migrationBuilder.AddForeignKey(
                name: "FK_ListasPrecios_ListasPrecios_IdListaBase",
                table: "ListasPrecios",
                column: "IdListaBase",
                principalTable: "ListasPrecios",
                principalColumn: "IdListaPrecio",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ListasPrecios_ListasPrecios_IdListaBase",
                table: "ListasPrecios");

            migrationBuilder.DropTable(
                name: "DiferencialesListaPrecio");

            migrationBuilder.DropIndex(
                name: "IX_ListasPrecios_IdListaBase",
                table: "ListasPrecios");

            migrationBuilder.DropColumn(
                name: "IdListaBase",
                table: "ListasPrecios");

            migrationBuilder.DropColumn(
                name: "CodigoLegacyVfp",
                table: "Lineas");
        }
    }
}
