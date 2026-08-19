using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdmiteCuentaCorrienteCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AdmiteCuentaCorriente",
                table: "Clientes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Los que YA tenían límite de crédito cargado en alguna sucursal obviamente admiten
            // cuenta corriente: sin este backfill quedarían en false y el ABM rechazaría editar
            // el límite que ya tienen (ver ClienteEnCuentaService.UpsertAsync).
            migrationBuilder.Sql(@"
                UPDATE c SET c.AdmiteCuentaCorriente = 1
                FROM Clientes c
                WHERE EXISTS (SELECT 1 FROM ClientesEnCuenta cc WHERE cc.IdCliente = c.IdCliente);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdmiteCuentaCorriente",
                table: "Clientes");
        }
    }
}
