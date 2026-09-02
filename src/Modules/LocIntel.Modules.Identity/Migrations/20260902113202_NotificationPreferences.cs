using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class NotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    sms_alerts = table.Column<bool>(type: "boolean", nullable: false),
                    sms_marketplace = table.Column<bool>(type: "boolean", nullable: false),
                    sms_network = table.Column<bool>(type: "boolean", nullable: false),
                    browser_alerts = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_notification_preferences_org_id_user_id",
                schema: "identity",
                table: "notification_preferences",
                columns: new[] { "org_id", "user_id" },
                unique: true
            );
            // RLS (the new-migration skill checklist): every org-scoped table
            migrationBuilder.EnableTenantRls("identity", "notification_preferences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "notification_preferences", schema: "identity");
        }
    }
}
