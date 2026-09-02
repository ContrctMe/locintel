using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class HierarchySitesAndTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateTable(
                name: "hierarchies",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    levels = table.Column<string[]>(type: "text[]", nullable: false),
                    is_authoritative = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hierarchies", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "hierarchy_nodes",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hierarchy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hierarchy_nodes", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "site_open_windows",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ends_at_utc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_open_windows", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "site_schedules",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    rrule = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    anchor_date = table.Column<DateOnly>(type: "date", nullable: false),
                    opens_local = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: false
                    ),
                    closes_local = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: false
                    ),
                    ex_dates = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_schedules", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "sites",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    time_zone = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(24)",
                        maxLength: 24,
                        nullable: false
                    ),
                    address_line1 = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: true
                    ),
                    city = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: true
                    ),
                    postal_code = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    country_code = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: true
                    ),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sites", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_hierarchies_org_id",
                schema: "tenancy",
                table: "hierarchies",
                column: "org_id",
                unique: true,
                filter: "is_authoritative"
            );

            migrationBuilder.CreateIndex(
                name: "IX_hierarchy_nodes_hierarchy_id",
                schema: "tenancy",
                table: "hierarchy_nodes",
                column: "hierarchy_id"
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_hierarchy_nodes_path",
                    schema: "tenancy",
                    table: "hierarchy_nodes",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_site_open_windows_org_id_starts_at_utc_ends_at_utc",
                schema: "tenancy",
                table: "site_open_windows",
                columns: new[] { "org_id", "starts_at_utc", "ends_at_utc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_site_open_windows_site_id",
                schema: "tenancy",
                table: "site_open_windows",
                column: "site_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_site_schedules_site_id",
                schema: "tenancy",
                table: "site_schedules",
                column: "site_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_sites_node_id",
                schema: "tenancy",
                table: "sites",
                column: "node_id"
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_sites_path",
                    schema: "tenancy",
                    table: "sites",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            // RLS (new-migration skill checklist): all five tables are org-scoped.
            migrationBuilder.EnableTenantRls("tenancy", "hierarchies");
            migrationBuilder.EnableTenantRls("tenancy", "hierarchy_nodes");
            migrationBuilder.EnableTenantRls("tenancy", "sites");
            migrationBuilder.EnableTenantRls("tenancy", "site_schedules");
            migrationBuilder.EnableTenantRls("tenancy", "site_open_windows");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "hierarchies", schema: "tenancy");

            migrationBuilder.DropTable(name: "hierarchy_nodes", schema: "tenancy");

            migrationBuilder.DropTable(name: "site_open_windows", schema: "tenancy");

            migrationBuilder.DropTable(name: "site_schedules", schema: "tenancy");

            migrationBuilder.DropTable(name: "sites", schema: "tenancy");

            migrationBuilder.AlterDatabase().OldAnnotation("Npgsql:PostgresExtension:ltree", ",,");
        }
    }
}
