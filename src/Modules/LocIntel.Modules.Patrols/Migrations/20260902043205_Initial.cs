using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Patrols.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "patrols");

            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateTable(
                name: "patrols",
                schema: "patrols",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scheduled_start_local = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: true
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    started_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    started_by = table.Column<Guid>(type: "uuid", nullable: false),
                    started_by_tier = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    ended_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    summary = table.Column<string>(
                        type: "character varying(4000)",
                        maxLength: 4000,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patrols", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "routes",
                schema: "patrols",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    checkpoints = table.Column<string>(type: "jsonb", nullable: false),
                    expected_minutes = table.Column<int>(type: "integer", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routes", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "scans",
                schema: "patrols",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patrol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    scanned_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    distance_m = table.Column<double>(type: "double precision", nullable: true),
                    note = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scans", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "schedules",
                schema: "patrols",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rrule = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    anchor_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_local = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: false
                    ),
                    ex_dates = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedules", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_patrols_org_id_site_id_business_date",
                schema: "patrols",
                table: "patrols",
                columns: new[] { "org_id", "site_id", "business_date" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_patrols_path",
                    schema: "patrols",
                    table: "patrols",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_routes_org_id_site_id",
                schema: "patrols",
                table: "routes",
                columns: new[] { "org_id", "site_id" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_routes_path",
                    schema: "patrols",
                    table: "routes",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_scans_patrol_id_scanned_at",
                schema: "patrols",
                table: "scans",
                columns: new[] { "patrol_id", "scanned_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_schedules_org_id_site_id",
                schema: "patrols",
                table: "schedules",
                columns: new[] { "org_id", "site_id" }
            );
            // RLS (the new-migration skill checklist): every org-scoped table
            migrationBuilder.EnableTenantRls("patrols", "routes");
            migrationBuilder.EnableTenantRls("patrols", "schedules");
            migrationBuilder.EnableTenantRls("patrols", "patrols");
            migrationBuilder.EnableTenantRls("patrols", "scans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "patrols", schema: "patrols");

            migrationBuilder.DropTable(name: "routes", schema: "patrols");

            migrationBuilder.DropTable(name: "scans", schema: "patrols");

            migrationBuilder.DropTable(name: "schedules", schema: "patrols");
        }
    }
}
