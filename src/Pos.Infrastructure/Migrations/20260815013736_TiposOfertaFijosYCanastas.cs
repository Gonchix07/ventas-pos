using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TiposOfertaFijosYCanastas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Codigo",
                table: "TiposOferta",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Seleccionable",
                table: "TiposOferta",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ItemsOfertas",
                columns: table => new
                {
                    IdItem = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdAccion = table.Column<int>(type: "int", nullable: false),
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdOferta = table.Column<int>(type: "int", nullable: false),
                    IdArticulo = table.Column<int>(type: "int", nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CantidadBonificada = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemsOfertas", x => x.IdItem);
                    table.ForeignKey(
                        name: "FK_ItemsOfertas_AccionesOfertas_IdAccion",
                        column: x => x.IdAccion,
                        principalTable: "AccionesOfertas",
                        principalColumn: "IdAccion",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemsOfertas_IdAccion",
                table: "ItemsOfertas",
                column: "IdAccion");

            // Las filas que ya existían quedarían con Codigo = 0 (ningún comportamiento) y las ofertas
            // viejas dejarían de aplicar. Se mapean por descripción a su código actual; el "Combo" que
            // nunca se implementó pasa a ser la Mix Canasta, y la Bonificación N+M queda como legacy
            // (sigue funcionando, pero ya no se ofrece en el ABM). El resto lo completa el seeder.
            migrationBuilder.Sql(@"
UPDATE TiposOferta SET Codigo = 1, Seleccionable = 1 WHERE Descripcion = 'Descuento';
UPDATE TiposOferta SET Codigo = 3, Seleccionable = 0 WHERE Descripcion IN ('Bonificacion', 'Bonificación');
UPDATE TiposOferta SET Codigo = 2, Seleccionable = 1, Descripcion = 'Mix Canasta' WHERE Descripcion = 'Combo';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemsOfertas");

            migrationBuilder.DropColumn(
                name: "Codigo",
                table: "TiposOferta");

            migrationBuilder.DropColumn(
                name: "Seleccionable",
                table: "TiposOferta");
        }
    }
}
