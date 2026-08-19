using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnaTarjetaActivaPorCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: true — el scaffold genera `false`, que dejaría las 149.200 tarjetas ya
            // cargadas del padrón como anuladas (ningún cliente identificaría su tarjeta en Caja).
            // Las que ya están son las vigentes.
            migrationBuilder.AddColumn<bool>(
                name: "Activa",
                table: "TarjetasClientes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaBajaUtc",
                table: "TarjetasClientes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TarjetasClientes_IdCliente_Activa",
                table: "TarjetasClientes",
                columns: new[] { "IdCliente", "Activa" });

            migrationBuilder.CreateIndex(
                name: "IX_TarjetasClientes_NroTarjeta_Activa",
                table: "TarjetasClientes",
                columns: new[] { "NroTarjeta", "Activa" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TarjetasClientes_IdCliente_Activa",
                table: "TarjetasClientes");

            migrationBuilder.DropIndex(
                name: "IX_TarjetasClientes_NroTarjeta_Activa",
                table: "TarjetasClientes");

            migrationBuilder.DropColumn(
                name: "Activa",
                table: "TarjetasClientes");

            migrationBuilder.DropColumn(
                name: "FechaBajaUtc",
                table: "TarjetasClientes");
        }
    }
}
