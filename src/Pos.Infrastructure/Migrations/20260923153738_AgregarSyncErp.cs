using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarSyncErp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoErp",
                table: "Sectores",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IdErp",
                table: "Presentaciones",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoErp",
                table: "ModosIva",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoErp",
                table: "Lineas",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoErp",
                table: "Familias",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoErp",
                table: "CondicionesIva",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstadoErp",
                table: "Clientes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IdErp",
                table: "Clientes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstadoErp",
                table: "Articulos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IdErp",
                table: "Articulos",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SyncCheckpoints",
                columns: table => new
                {
                    IdSyncCheckpoint = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Fuente = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UltimoWatermarkUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimaCorridaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimoResultado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Insertados = table.Column<int>(type: "int", nullable: false),
                    Actualizados = table.Column<int>(type: "int", nullable: false),
                    Errores = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCheckpoints", x => x.IdSyncCheckpoint);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sectores_CodigoErp",
                table: "Sectores",
                column: "CodigoErp",
                unique: true,
                filter: "[CodigoErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Presentaciones_IdErp",
                table: "Presentaciones",
                column: "IdErp",
                unique: true,
                filter: "[IdErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ModosIva_CodigoErp",
                table: "ModosIva",
                column: "CodigoErp",
                unique: true,
                filter: "[CodigoErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Lineas_CodigoErp",
                table: "Lineas",
                column: "CodigoErp",
                unique: true,
                filter: "[CodigoErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Familias_CodigoErp",
                table: "Familias",
                column: "CodigoErp",
                unique: true,
                filter: "[CodigoErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CondicionesIva_CodigoErp",
                table: "CondicionesIva",
                column: "CodigoErp",
                unique: true,
                filter: "[CodigoErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_IdErp",
                table: "Clientes",
                column: "IdErp",
                unique: true,
                filter: "[IdErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Articulos_IdErp",
                table: "Articulos",
                column: "IdErp",
                unique: true,
                filter: "[IdErp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SyncCheckpoints_Fuente",
                table: "SyncCheckpoints",
                column: "Fuente",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_Sectores_CodigoErp",
                table: "Sectores");

            migrationBuilder.DropIndex(
                name: "IX_Presentaciones_IdErp",
                table: "Presentaciones");

            migrationBuilder.DropIndex(
                name: "IX_ModosIva_CodigoErp",
                table: "ModosIva");

            migrationBuilder.DropIndex(
                name: "IX_Lineas_CodigoErp",
                table: "Lineas");

            migrationBuilder.DropIndex(
                name: "IX_Familias_CodigoErp",
                table: "Familias");

            migrationBuilder.DropIndex(
                name: "IX_CondicionesIva_CodigoErp",
                table: "CondicionesIva");

            migrationBuilder.DropIndex(
                name: "IX_Clientes_IdErp",
                table: "Clientes");

            migrationBuilder.DropIndex(
                name: "IX_Articulos_IdErp",
                table: "Articulos");

            migrationBuilder.DropColumn(
                name: "CodigoErp",
                table: "Sectores");

            migrationBuilder.DropColumn(
                name: "IdErp",
                table: "Presentaciones");

            migrationBuilder.DropColumn(
                name: "CodigoErp",
                table: "ModosIva");

            migrationBuilder.DropColumn(
                name: "CodigoErp",
                table: "Lineas");

            migrationBuilder.DropColumn(
                name: "CodigoErp",
                table: "Familias");

            migrationBuilder.DropColumn(
                name: "CodigoErp",
                table: "CondicionesIva");

            migrationBuilder.DropColumn(
                name: "EstadoErp",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "IdErp",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "EstadoErp",
                table: "Articulos");

            migrationBuilder.DropColumn(
                name: "IdErp",
                table: "Articulos");
        }
    }
}
