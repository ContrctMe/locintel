using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Audit.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "audit");

            migrationBuilder.CreateTable(
                name: "access_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_tier = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    method = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    path = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_log", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "authz_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_tier = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    outcome = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    scope_summary = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_log", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "change_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_tier = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_label = table.Column<string>(
                        type: "character varying(320)",
                        maxLength: 320,
                        nullable: true
                    ),
                    schema_name = table.Column<string>(
                        type: "character varying(63)",
                        maxLength: 63,
                        nullable: false
                    ),
                    table_name = table.Column<string>(
                        type: "character varying(63)",
                        maxLength: 63,
                        nullable: false
                    ),
                    row_id = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: false
                    ),
                    operation = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    diff = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_log", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "domain_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_tier = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_name = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_log", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "org_audit_config",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_grants = table.Column<bool>(type: "boolean", nullable: false),
                    log_reads = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_audit_config", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_access_log_org_id_occurred_at",
                schema: "audit",
                table: "access_log",
                columns: new[] { "org_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_authz_log_org_id_occurred_at",
                schema: "audit",
                table: "authz_log",
                columns: new[] { "org_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_change_log_org_id_occurred_at",
                schema: "audit",
                table: "change_log",
                columns: new[] { "org_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_domain_log_org_id_occurred_at",
                schema: "audit",
                table: "domain_log",
                columns: new[] { "org_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_org_audit_config_org_id",
                schema: "audit",
                table: "org_audit_config",
                column: "org_id",
                unique: true
            );

            // RLS (new-migration skill checklist): all five tables org-scoped.
            // Rows with NULL org (platform work) are invisible to tenants -
            // fail closed, readable only by the owner role.
            migrationBuilder.EnableTenantRls("audit", "change_log");
            // Platform-global rows (users, memberships, directory sync) audit
            // with NULL org: insertable by the app, invisible to tenants
            // (the tenant SELECT policy never matches NULL - fail closed).
            migrationBuilder.Sql(
                """
                CREATE POLICY platform_rows_insert ON "audit"."change_log"
                    FOR INSERT WITH CHECK (org_id IS NULL);
                """
            );
            migrationBuilder.EnableTenantRls("audit", "domain_log");
            migrationBuilder.EnableTenantRls("audit", "authz_log");
            migrationBuilder.EnableTenantRls("audit", "access_log");
            migrationBuilder.EnableTenantRls("audit", "org_audit_config");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "access_log", schema: "audit");

            migrationBuilder.DropTable(name: "authz_log", schema: "audit");

            migrationBuilder.DropTable(name: "change_log", schema: "audit");

            migrationBuilder.DropTable(name: "domain_log", schema: "audit");

            migrationBuilder.DropTable(name: "org_audit_config", schema: "audit");
        }
    }
}
