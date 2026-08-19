using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CertificadoCaeEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificadoNombreArchivo",
                table: "Empresas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificadoPasswordProtegida",
                table: "Empresas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CertificadoSubidoUtc",
                table: "Empresas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CertificadoVencimiento",
                table: "Empresas",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificadoNombreArchivo",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "CertificadoPasswordProtegida",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "CertificadoSubidoUtc",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "CertificadoVencimiento",
                table: "Empresas");
        }
    }
}
