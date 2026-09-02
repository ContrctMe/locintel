using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Incidents.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "incidents");

            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_attachments", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "incidents",
                schema: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hierarchy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    category = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    severity = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    source = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    narrative = table.Column<string>(type: "text", nullable: false),
                    location_detail = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    loss_amount = table.Column<decimal>(
                        type: "numeric(14,2)",
                        precision: 14,
                        scale: 2,
                        nullable: true
                    ),
                    recovered_amount = table.Column<decimal>(
                        type: "numeric(14,2)",
                        precision: 14,
                        scale: 2,
                        nullable: true
                    ),
                    currency = table.Column<string>(
                        type: "character varying(3)",
                        maxLength: 3,
                        nullable: false
                    ),
                    police_report_number = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closure_reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    legal_hold = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_incidents", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "notes",
                schema: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_notes", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_attachments_incident_id_file_id",
                schema: "incidents",
                table: "attachments",
                columns: new[] { "incident_id", "file_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_attachments_org_id_incident_id",
                schema: "incidents",
                table: "attachments",
                columns: new[] { "org_id", "incident_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_incidents_org_id_business_date_category",
                schema: "incidents",
                table: "incidents",
                columns: new[] { "org_id", "business_date", "category" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_incidents_org_id_site_id_occurred_at",
                schema: "incidents",
                table: "incidents",
                columns: new[] { "org_id", "site_id", "occurred_at" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_incidents_path",
                    schema: "incidents",
                    table: "incidents",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_notes_org_id_incident_id",
                schema: "incidents",
                table: "notes",
                columns: new[] { "org_id", "incident_id" }
            );
            // RLS (the new-migration skill checklist): every org-scoped table
            migrationBuilder.EnableTenantRls("incidents", "incidents");
            migrationBuilder.EnableTenantRls("incidents", "notes");
            migrationBuilder.EnableTenantRls("incidents", "attachments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "attachments", schema: "incidents");

            migrationBuilder.DropTable(name: "incidents", schema: "incidents");

            migrationBuilder.DropTable(name: "notes", schema: "incidents");
        }
    }
}
