using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Ingest.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sync_interval_hours",
                schema: "ingest",
                table: "site_connectors",
                type: "integer",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sync_interval_hours",
                schema: "ingest",
                table: "site_connectors"
            );
        }
    }
}
