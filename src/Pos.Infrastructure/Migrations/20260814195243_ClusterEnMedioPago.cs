using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClusterEnMedioPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClusterIdCluster",
                table: "MediosPago",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdCluster",
                table: "MediosPago",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediosPago_ClusterIdCluster",
                table: "MediosPago",
                column: "ClusterIdCluster");

            migrationBuilder.AddForeignKey(
                name: "FK_MediosPago_Clusters_ClusterIdCluster",
                table: "MediosPago",
                column: "ClusterIdCluster",
                principalTable: "Clusters",
                principalColumn: "IdCluster",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediosPago_Clusters_ClusterIdCluster",
                table: "MediosPago");

            migrationBuilder.DropIndex(
                name: "IX_MediosPago_ClusterIdCluster",
                table: "MediosPago");

            migrationBuilder.DropColumn(
                name: "ClusterIdCluster",
                table: "MediosPago");

            migrationBuilder.DropColumn(
                name: "IdCluster",
                table: "MediosPago");
        }
    }
}
