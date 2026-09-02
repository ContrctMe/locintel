using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grant_exceptions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(
                        type: "character varying(60)",
                        maxLength: 60,
                        nullable: false
                    ),
                    action = table.Column<string>(
                        type: "character varying(60)",
                        maxLength: 60,
                        nullable: false
                    ),
                    scope_path = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_grant_exceptions", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "membership_roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_path = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_roles", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "role_grants",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(
                        type: "character varying(60)",
                        maxLength: 60,
                        nullable: false
                    ),
                    action = table.Column<string>(
                        type: "character varying(60)",
                        maxLength: 60,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_grants", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_grant_exceptions_user_id_org_id_expires_at",
                schema: "identity",
                table: "grant_exceptions",
                columns: new[] { "user_id", "org_id", "expires_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_membership_roles_membership_id",
                schema: "identity",
                table: "membership_roles",
                column: "membership_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_role_grants_role_id",
                schema: "identity",
                table: "role_grants",
                column: "role_id"
            );

            migrationBuilder.CreateIndex(
                name: "IX_roles_org_id_name",
                schema: "identity",
                table: "roles",
                columns: new[] { "org_id", "name" },
                unique: true
            );

            // RLS (new-migration skill checklist). users/memberships remain
            // platform-global (identity is global, ADR 35); access tables are org data.
            migrationBuilder.EnableTenantRls("identity", "roles");
            migrationBuilder.EnableTenantRls("identity", "role_grants");
            migrationBuilder.EnableTenantRls("identity", "membership_roles");
            migrationBuilder.EnableTenantRls("identity", "grant_exceptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "grant_exceptions", schema: "identity");

            migrationBuilder.DropTable(name: "membership_roles", schema: "identity");

            migrationBuilder.DropTable(name: "role_grants", schema: "identity");

            migrationBuilder.DropTable(name: "roles", schema: "identity");
        }
    }
}
