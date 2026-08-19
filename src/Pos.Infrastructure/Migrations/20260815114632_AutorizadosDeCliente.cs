using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutorizadosDeCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Autorizados",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAlta",
                table: "Autorizados",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Los autorizados que ya existieran quedarían inactivos y con fecha 0001-01-01: se los
            // deja activos y con la fecha en que se cargaron.
            migrationBuilder.Sql(
                "UPDATE Autorizados SET Activo = 1, FechaAlta = CreatedAtUtc WHERE FechaAlta = '0001-01-01';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Autorizados");

            migrationBuilder.DropColumn(
                name: "FechaAlta",
                table: "Autorizados");
        }
    }
}
