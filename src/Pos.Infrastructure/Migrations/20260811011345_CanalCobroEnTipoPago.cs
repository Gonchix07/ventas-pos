using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CanalCobroEnTipoPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue 1 = CanalCobro.Manual. El scaffold pone 0, que NO es un valor válido del
            // enum (arranca en 1) y dejaría los tipos existentes con un canal inexistente.
            migrationBuilder.AddColumn<int>(
                name: "Canal",
                table: "TiposPago",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Canal",
                table: "TiposPago");
        }
    }
}
