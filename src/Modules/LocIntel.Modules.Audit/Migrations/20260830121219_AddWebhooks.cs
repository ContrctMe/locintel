using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Audit.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_name = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    ok = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    encrypted_secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    events = table.Column<string[]>(type: "text[]", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_endpoints", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_endpoint_id_occurred_at",
                schema: "audit",
                table: "webhook_deliveries",
                columns: new[] { "endpoint_id", "occurred_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_webhook_endpoints_org_id",
                schema: "audit",
                table: "webhook_endpoints",
                column: "org_id"
            );
            migrationBuilder.EnableTenantRls("audit", "webhook_endpoints");
            migrationBuilder.EnableTenantRls("audit", "webhook_deliveries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "webhook_deliveries", schema: "audit");

            migrationBuilder.DropTable(name: "webhook_endpoints", schema: "audit");
        }
    }
}
