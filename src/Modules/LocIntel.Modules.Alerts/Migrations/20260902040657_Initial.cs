using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Alerts.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "alerts");

            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateTable(
                name: "alert_reads",
                schema: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_reads", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "alerts",
                schema: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    severity = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    body = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    path = table.Column<string>(type: "ltree", nullable: true),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bulletin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "bulletin_acknowledgements",
                schema: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bulletin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    acknowledged_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulletin_acknowledgements", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "bulletins",
                schema: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    severity = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    body = table.Column<string>(type: "text", nullable: false),
                    scope_path = table.Column<string>(type: "ltree", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    withdrawn_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    withdrawn_by = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulletins", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_alert_reads_alert_id_user_id",
                schema: "alerts",
                table: "alert_reads",
                columns: new[] { "alert_id", "user_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_alert_reads_org_id_user_id",
                schema: "alerts",
                table: "alert_reads",
                columns: new[] { "org_id", "user_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_alerts_org_id_created_at",
                schema: "alerts",
                table: "alerts",
                columns: new[] { "org_id", "created_at" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_alerts_path",
                    schema: "alerts",
                    table: "alerts",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_bulletin_acknowledgements_bulletin_id_user_id",
                schema: "alerts",
                table: "bulletin_acknowledgements",
                columns: new[] { "bulletin_id", "user_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bulletins_org_id_status_expires_at",
                schema: "alerts",
                table: "bulletins",
                columns: new[] { "org_id", "status", "expires_at" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_bulletins_scope_path",
                    schema: "alerts",
                    table: "bulletins",
                    column: "scope_path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");
            // RLS (the new-migration skill checklist): every org-scoped table
            migrationBuilder.EnableTenantRls("alerts", "alerts");
            migrationBuilder.EnableTenantRls("alerts", "alert_reads");
            migrationBuilder.EnableTenantRls("alerts", "bulletins");
            migrationBuilder.EnableTenantRls("alerts", "bulletin_acknowledgements");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "alert_reads", schema: "alerts");

            migrationBuilder.DropTable(name: "alerts", schema: "alerts");

            migrationBuilder.DropTable(name: "bulletin_acknowledgements", schema: "alerts");

            migrationBuilder.DropTable(name: "bulletins", schema: "alerts");
        }
    }
}
