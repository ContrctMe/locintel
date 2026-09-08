using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LocIntel.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class SiteLocationGeography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.AddColumn<Point>(
                name: "location",
                schema: "tenancy",
                table: "sites",
                type: "geography (point, 4326)",
                nullable: true
            );

            // Backfill from the doubles (ADR 50): x = longitude, y = latitude.
            // From here on TenancyDbContext keeps the column in step on save.
            migrationBuilder.Sql(
                """
                UPDATE tenancy.sites
                SET location = ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)::geography
                WHERE latitude IS NOT NULL AND longitude IS NOT NULL;
                """
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_sites_location",
                    schema: "tenancy",
                    table: "sites",
                    column: "location"
                )
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The extension stays: Npgsql emits no DROP EXTENSION for a removed
            // annotation, and ADR 50 says never drop it while another schema
            // could depend on it. Column and index revert fully.
            migrationBuilder.DropIndex(
                name: "IX_sites_location",
                schema: "tenancy",
                table: "sites"
            );

            migrationBuilder.DropColumn(name: "location", schema: "tenancy", table: "sites");

            migrationBuilder
                .AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:ltree", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
