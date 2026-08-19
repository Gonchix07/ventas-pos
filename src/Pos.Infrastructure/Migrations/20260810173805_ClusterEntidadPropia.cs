using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <summary>
    /// El cluster pasa a ser una entidad propia (tabla Clusters) en vez de vivir como una columna
    /// Descripcion duplicada en cada fila de ClusterClientes. Con el diseño viejo un cluster no
    /// podía existir sin miembros ni renombrarse.
    ///
    /// OJO: el scaffold automático de EF borraba ClusterClientes.Descripcion ANTES de crear
    /// Clusters (perdía los nombres) y dejaba la tabla nueva vacía, con lo que la FK fallaba contra
    /// las filas existentes. Los pasos de abajo están ordenados a mano para migrar los datos.
    /// </summary>
    public partial class ClusterEntidadPropia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Tabla nueva.
            migrationBuilder.CreateTable(
                name: "Clusters",
                columns: table => new
                {
                    IdCluster = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clusters", x => x.IdCluster);
                });

            // 2) Migrar los clusters existentes PRESERVANDO el IdCluster: las ofertas los referencian
            //    por número (AlcancesOfertas.IdCluster), así que renumerar rompería sus alcances.
            //    Con varias filas por cluster (el nombre estaba duplicado) se toma MIN como
            //    representante; si alguna vez quedaron desincronizadas, gana la primera alfabéticamente.
            migrationBuilder.Sql(@"
SET IDENTITY_INSERT [Clusters] ON;

INSERT INTO [Clusters] ([IdCluster], [Descripcion], [CreatedAtUtc])
SELECT cc.[IdCluster], MIN(cc.[Descripcion]), SYSUTCDATETIME()
FROM [ClusterClientes] cc
GROUP BY cc.[IdCluster];

SET IDENTITY_INSERT [Clusters] OFF;
");

            // 3) Reposicionar el identity para que los clusters nuevos no choquen con los migrados.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Clusters])
    DBCC CHECKIDENT ('[Clusters]', RESEED);
");

            // 4) Ahora sí se puede tirar la columna duplicada y atar la FK.
            migrationBuilder.DropColumn(
                name: "Descripcion",
                table: "ClusterClientes");

            migrationBuilder.AddForeignKey(
                name: "FK_ClusterClientes_Clusters_IdCluster",
                table: "ClusterClientes",
                column: "IdCluster",
                principalTable: "Clusters",
                principalColumn: "IdCluster",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClusterClientes_Clusters_IdCluster",
                table: "ClusterClientes");

            migrationBuilder.AddColumn<string>(
                name: "Descripcion",
                table: "ClusterClientes",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // Devolver el nombre a cada fila de pertenencia antes de perder la tabla.
            migrationBuilder.Sql(@"
UPDATE cc SET cc.[Descripcion] = c.[Descripcion]
FROM [ClusterClientes] cc
INNER JOIN [Clusters] c ON c.[IdCluster] = cc.[IdCluster];
");

            // Los clusters sin miembros no tienen dónde volver: el modelo viejo no podía representarlos.
            migrationBuilder.DropTable(name: "Clusters");
        }
    }
}
