using System;
using Microsoft.EntityFrameworkCore.Migrations;
using LocIntel.Platform.Data;

#nullable disable

namespace LocIntel.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class SiteAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attributes",
                schema: "tenancy",
                table: "sites",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}"
            );

            migrationBuilder.CreateTable(
                name: "site_attribute_definitions",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(
                        type: "character varying(60)",
                        maxLength: 60,
                        nullable: false
                    ),
                    label = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    type = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    @public = table.Column<bool>(name: "public", type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_attribute_definitions", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_site_attribute_definitions_org_id_key",
                schema: "tenancy",
                table: "site_attribute_definitions",
                columns: new[] { "org_id", "key" },
                unique: true
            );
            // RLS (the new-migration skill checklist): org-scoped table
            migrationBuilder.EnableTenantRls("tenancy", "site_attribute_definitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "site_attribute_definitions", schema: "tenancy");

            migrationBuilder.DropColumn(name: "attributes", schema: "tenancy", table: "sites");
        }
    }
}
