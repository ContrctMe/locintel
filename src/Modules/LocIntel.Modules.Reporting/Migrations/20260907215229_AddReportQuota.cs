using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class AddReportQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-quota jobs are grandfathered; do not retroactively charge existing work.
            migrationBuilder.CreateTable(
                name: "quota_entries",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_month = table.Column<DateOnly>(type: "date", nullable: false),
                    consumed = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_entries", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_quota_entries_org_id_job_id",
                schema: "reporting",
                table: "quota_entries",
                columns: new[] { "org_id", "job_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_quota_entries_org_id_period_month",
                schema: "reporting",
                table: "quota_entries",
                columns: new[] { "org_id", "period_month" }
            );
            migrationBuilder.EnableTenantRls("reporting", "quota_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "quota_entries", schema: "reporting");
        }
    }
}
