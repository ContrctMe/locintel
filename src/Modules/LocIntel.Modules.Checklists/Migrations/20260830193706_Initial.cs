using System;
using Microsoft.EntityFrameworkCore.Migrations;
using LocIntel.Platform.Data;

#nullable disable

namespace LocIntel.Modules.Checklists.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "checklists");

            migrationBuilder.CreateTable(
                name: "item_checks",
                schema: "checklists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    item_index = table.Column<int>(type: "integer", nullable: false),
                    checked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_checks", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "checklists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    items = table.Column<string[]>(type: "text[]", nullable: false),
                    scope_path = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_templates", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_item_checks_org_id_site_id_business_date",
                schema: "checklists",
                table: "item_checks",
                columns: new[] { "org_id", "site_id", "business_date" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_item_checks_template_id_site_id_business_date_item_index",
                schema: "checklists",
                table: "item_checks",
                columns: new[] { "template_id", "site_id", "business_date", "item_index" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_templates_org_id",
                schema: "checklists",
                table: "templates",
                column: "org_id"
            );
            // RLS (the new-migration skill checklist): every org-scoped table
            migrationBuilder.EnableTenantRls("checklists", "templates");
            migrationBuilder.EnableTenantRls("checklists", "item_checks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "item_checks", schema: "checklists");

            migrationBuilder.DropTable(name: "templates", schema: "checklists");
        }
    }
}
