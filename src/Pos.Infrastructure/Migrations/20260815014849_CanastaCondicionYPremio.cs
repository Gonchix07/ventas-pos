using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CanastaCondicionYPremio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CantidadBonificada",
                table: "ItemsOfertas");

            migrationBuilder.AddColumn<int>(
                name: "Rol",
                table: "ItemsOfertas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // La canasta pasó a tener dos lados (la que activa y la que se bonifica), así que el
            // "cuánto bonifica cada renglón" ya no existe. Rol = 0 no es ningún lado: si quedó alguna
            // fila de la versión anterior (la tabla se creó hoy), se la deja como condición.
            migrationBuilder.Sql("UPDATE ItemsOfertas SET Rol = 1 WHERE Rol = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rol",
                table: "ItemsOfertas");

            migrationBuilder.AddColumn<decimal>(
                name: "CantidadBonificada",
                table: "ItemsOfertas",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
