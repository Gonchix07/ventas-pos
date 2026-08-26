using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorregirUnidadXBultoArticuloExistente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La migración anterior (AgregarUnidadXBultoAArticulo) agregó la columna con
            // defaultValue: 0 por error — debía ser 1 ("no viene en bulto"), mismo criterio que
            // Presentacion.UnidadXBulto. Cualquier artículo que ya haya quedado en 0 (todos los
            // existentes al momento de esa migración, y ninguno editado a mano todavía) se corrige acá.
            migrationBuilder.Sql("UPDATE dbo.Articulos SET UnidadXBulto = 1 WHERE UnidadXBulto = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No reversible de forma segura: no hay manera de distinguir "estaba en 0 por el bug"
            // de "se editó a 1 legítimamente después" — se deja como no-op.
        }
    }
}
