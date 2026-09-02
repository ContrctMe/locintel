using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Entities.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "entities");

            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateTable(
                name: "access_grants",
                schema: "entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_grants", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "entities",
                schema: "entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    display_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    descriptors = table.Column<string>(type: "jsonb", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
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
                    table.PrimaryKey("PK_entities", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "incident_links",
                schema: "entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    role = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    note = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_links", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_org_id_user_id_entity_id",
                schema: "entities",
                table: "access_grants",
                columns: new[] { "org_id", "user_id", "entity_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_entities_org_id_expires_at",
                schema: "entities",
                table: "entities",
                columns: new[] { "org_id", "expires_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_entities_org_id_kind",
                schema: "entities",
                table: "entities",
                columns: new[] { "org_id", "kind" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_incident_links_entity_id_incident_id",
                schema: "entities",
                table: "incident_links",
                columns: new[] { "entity_id", "incident_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_incident_links_org_id_incident_id",
                schema: "entities",
                table: "incident_links",
                columns: new[] { "org_id", "incident_id" }
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_incident_links_path",
                    schema: "entities",
                    table: "incident_links",
                    column: "path"
                )
                .Annotation("Npgsql:IndexMethod", "gist");
            // RLS (the new-migration skill checklist): every org-scoped table
            migrationBuilder.EnableTenantRls("entities", "entities");
            migrationBuilder.EnableTenantRls("entities", "incident_links");
            migrationBuilder.EnableTenantRls("entities", "access_grants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "access_grants", schema: "entities");

            migrationBuilder.DropTable(name: "entities", schema: "entities");

            migrationBuilder.DropTable(name: "incident_links", schema: "entities");
        }
    }
}
