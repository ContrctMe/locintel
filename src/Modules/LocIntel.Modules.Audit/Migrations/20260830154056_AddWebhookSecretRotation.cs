using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Audit.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookSecretRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "previous_encrypted_secret",
                schema: "audit",
                table: "webhook_endpoints",
                type: "bytea",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "previous_secret_expires_at",
                schema: "audit",
                table: "webhook_endpoints",
                type: "timestamp with time zone",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "previous_encrypted_secret",
                schema: "audit",
                table: "webhook_endpoints"
            );

            migrationBuilder.DropColumn(
                name: "previous_secret_expires_at",
                schema: "audit",
                table: "webhook_endpoints"
            );
        }
    }
}
