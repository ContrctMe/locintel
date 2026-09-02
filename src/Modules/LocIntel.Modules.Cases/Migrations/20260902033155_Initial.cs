using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Cases.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "cases");

            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateTable(
                name: "case_entities",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_entities", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "case_evidence",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_evidence", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "case_incidents",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_incidents", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "case_members",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_members", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "case_notes",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(
                        type: "character varying(4000)",
                        maxLength: 4000,
                        nullable: false
                    ),
                    created_at = table.Column<DateTimeOffset>(
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
                    table.PrimaryKey("PK_case_notes", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "case_tasks",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    done_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    done_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
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
                    table.PrimaryKey("PK_case_tasks", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "cases",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    summary = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    priority = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    disposition = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: true
                    ),
                    closed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closure_note = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    legal_hold = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_cases", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "custody_events",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_tier = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    detail = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custody_events", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_entities_case_id_entity_id",
                schema: "cases",
                table: "case_entities",
                columns: new[] { "case_id", "entity_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_entities_org_id_entity_id",
                schema: "cases",
                table: "case_entities",
                columns: new[] { "org_id", "entity_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_evidence_case_id_file_id",
                schema: "cases",
                table: "case_evidence",
                columns: new[] { "case_id", "file_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_evidence_org_id_file_id",
                schema: "cases",
                table: "case_evidence",
                columns: new[] { "org_id", "file_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_incidents_case_id_incident_id",
                schema: "cases",
                table: "case_incidents",
                columns: new[] { "case_id", "incident_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_incidents_org_id_incident_id",
                schema: "cases",
                table: "case_incidents",
                columns: new[] { "org_id", "incident_id" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_case_incidents_path",
                    schema: "cases",
                    table: "case_incidents",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_case_members_case_id_user_id",
                schema: "cases",
                table: "case_members",
                columns: new[] { "case_id", "user_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_members_org_id_user_id",
                schema: "cases",
                table: "case_members",
                columns: new[] { "org_id", "user_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_notes_org_id_case_id",
                schema: "cases",
                table: "case_notes",
                columns: new[] { "org_id", "case_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_case_tasks_org_id_case_id",
                schema: "cases",
                table: "case_tasks",
                columns: new[] { "org_id", "case_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_cases_org_id_status_updated_at",
                schema: "cases",
                table: "cases",
                columns: new[] { "org_id", "status", "updated_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custody_events_org_id_case_id_at",
                schema: "cases",
                table: "custody_events",
                columns: new[] { "org_id", "case_id", "at" }
            );
            // RLS (the new-migration skill checklist): every org-scoped table
            migrationBuilder.EnableTenantRls("cases", "cases");
            migrationBuilder.EnableTenantRls("cases", "case_incidents");
            migrationBuilder.EnableTenantRls("cases", "case_entities");
            migrationBuilder.EnableTenantRls("cases", "case_members");
            migrationBuilder.EnableTenantRls("cases", "case_tasks");
            migrationBuilder.EnableTenantRls("cases", "case_notes");
            migrationBuilder.EnableTenantRls("cases", "case_evidence");
            migrationBuilder.EnableTenantRls("cases", "custody_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "case_entities", schema: "cases");

            migrationBuilder.DropTable(name: "case_evidence", schema: "cases");

            migrationBuilder.DropTable(name: "case_incidents", schema: "cases");

            migrationBuilder.DropTable(name: "case_members", schema: "cases");

            migrationBuilder.DropTable(name: "case_notes", schema: "cases");

            migrationBuilder.DropTable(name: "case_tasks", schema: "cases");

            migrationBuilder.DropTable(name: "cases", schema: "cases");

            migrationBuilder.DropTable(name: "custody_events", schema: "cases");
        }
    }
}
