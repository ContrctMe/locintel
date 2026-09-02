using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Marketplace.Migrations
{
    /// <inheritdoc />
    public partial class Routing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "escalated_at",
                schema: "marketplace",
                table: "requests",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "escalation_count",
                schema: "marketplace",
                table: "requests",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "response_due_at",
                schema: "marketplace",
                table: "requests",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "site_country_code",
                schema: "marketplace",
                table: "requests",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_requests_status_response_due_at",
                schema: "marketplace",
                table: "requests",
                columns: new[] { "status", "response_due_at" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_requests_status_response_due_at",
                schema: "marketplace",
                table: "requests"
            );

            migrationBuilder.DropColumn(
                name: "escalated_at",
                schema: "marketplace",
                table: "requests"
            );

            migrationBuilder.DropColumn(
                name: "escalation_count",
                schema: "marketplace",
                table: "requests"
            );

            migrationBuilder.DropColumn(
                name: "response_due_at",
                schema: "marketplace",
                table: "requests"
            );

            migrationBuilder.DropColumn(
                name: "site_country_code",
                schema: "marketplace",
                table: "requests"
            );
        }
    }
}
