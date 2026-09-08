using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LocIntel.Modules.Spatial.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "spatial");

            migrationBuilder
                .AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "overlay_layers",
                schema: "spatial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    kind = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    style = table.Column<string>(type: "jsonb", nullable: true),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hierarchy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    deleted_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_layers", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_features",
                schema: "spatial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    geom = table.Column<Geometry>(
                        type: "geography (geometry, 4326)",
                        nullable: false
                    ),
                    properties = table.Column<string>(type: "jsonb", nullable: true),
                    hierarchy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_features", x => x.id);
                    table.ForeignKey(
                        name: "FK_overlay_features_overlay_layers_layer_id",
                        column: x => x.layer_id,
                        principalSchema: "spatial",
                        principalTable: "overlay_layers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_overlay_features_geom",
                    schema: "spatial",
                    table: "overlay_features",
                    column: "geom"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_overlay_features_layer_id",
                schema: "spatial",
                table: "overlay_features",
                column: "layer_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_features_org_id_layer_id",
                schema: "spatial",
                table: "overlay_features",
                columns: new[] { "org_id", "layer_id" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_overlay_features_path",
                    schema: "spatial",
                    table: "overlay_features",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_overlay_layers_org_id_name",
                schema: "spatial",
                table: "overlay_layers",
                columns: new[] { "org_id", "name" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_overlay_layers_path",
                    schema: "spatial",
                    table: "overlay_layers",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            // RLS (the new-migration skill checklist): every org-scoped table.
            // Both are tenant-scoped (ADR 48: one owning org per row); the
            // postgis extension itself is Tenancy's to create and never dropped
            // here (ADR 50), so Down() drops the tables only.
            migrationBuilder.EnableTenantRls("spatial", "overlay_layers");
            migrationBuilder.EnableTenantRls("spatial", "overlay_features");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "overlay_features", schema: "spatial");

            migrationBuilder.DropTable(name: "overlay_layers", schema: "spatial");
        }
    }
}
