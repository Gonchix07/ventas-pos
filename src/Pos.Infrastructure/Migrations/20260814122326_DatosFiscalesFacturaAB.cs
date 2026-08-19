using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DatosFiscalesFacturaAB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoPostal",
                table: "Sucursales",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Domicilio",
                table: "Sucursales",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Localidad",
                table: "Sucursales",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provincia",
                table: "Sucursales",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoPostal",
                table: "Empresas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CondicionIva",
                table: "Empresas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Domicilio",
                table: "Empresas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IngresosBrutos",
                table: "Empresas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InicioActividad",
                table: "Empresas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Localidad",
                table: "Empresas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provincia",
                table: "Empresas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provincia",
                table: "Clientes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodigoPostal",
                table: "Sucursales");

            migrationBuilder.DropColumn(
                name: "Domicilio",
                table: "Sucursales");

            migrationBuilder.DropColumn(
                name: "Localidad",
                table: "Sucursales");

            migrationBuilder.DropColumn(
                name: "Provincia",
                table: "Sucursales");

            migrationBuilder.DropColumn(
                name: "CodigoPostal",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "CondicionIva",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Domicilio",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "IngresosBrutos",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "InicioActividad",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Localidad",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Provincia",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Provincia",
                table: "Clientes");
        }
    }
}
