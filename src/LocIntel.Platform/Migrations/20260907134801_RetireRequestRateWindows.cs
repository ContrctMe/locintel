using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Platform.Migrations
{
    /// <inheritdoc />
    public partial class RetireRequestRateWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 54: ephemeral admission counters only. Customer entitlements
            // and business quotas are not deleted by this migration.
            migrationBuilder.DropTable(name: "rate_windows", schema: "platform");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rate_windows",
                schema: "platform",
                columns: table => new
                {
                    partition = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    window_start = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    count = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_windows", x => new { x.partition, x.window_start });
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_rate_windows_window_start",
                schema: "platform",
                table: "rate_windows",
                column: "window_start"
            );
        }
    }
}
