using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Entitlements.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "entitlements");

            migrationBuilder.CreateTable(
                name: "entitlement_exceptions",
                schema: "entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    value = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
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
                    table.PrimaryKey("PK_entitlement_exceptions", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "meter_rollups",
                schema: "entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    period_month = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    compacted_through = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meter_rollups", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "org_entitlements",
                schema: "entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    value = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    source = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_entitlements", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "usage_events",
                schema: "entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_events", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_entitlement_exceptions_org_id_code_expires_at",
                schema: "entitlements",
                table: "entitlement_exceptions",
                columns: new[] { "org_id", "code", "expires_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_meter_rollups_org_id_code_period_month",
                schema: "entitlements",
                table: "meter_rollups",
                columns: new[] { "org_id", "code", "period_month" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_org_entitlements_org_id_code",
                schema: "entitlements",
                table: "org_entitlements",
                columns: new[] { "org_id", "code" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_usage_events_org_id_code_occurred_at",
                schema: "entitlements",
                table: "usage_events",
                columns: new[] { "org_id", "code", "occurred_at" }
            );

            // RLS (new-migration skill checklist)
            migrationBuilder.EnableTenantRls("entitlements", "org_entitlements");
            migrationBuilder.EnableTenantRls("entitlements", "entitlement_exceptions");
            migrationBuilder.EnableTenantRls("entitlements", "usage_events");
            migrationBuilder.EnableTenantRls("entitlements", "meter_rollups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "entitlement_exceptions", schema: "entitlements");

            migrationBuilder.DropTable(name: "meter_rollups", schema: "entitlements");

            migrationBuilder.DropTable(name: "org_entitlements", schema: "entitlements");

            migrationBuilder.DropTable(name: "usage_events", schema: "entitlements");
        }
    }
}
