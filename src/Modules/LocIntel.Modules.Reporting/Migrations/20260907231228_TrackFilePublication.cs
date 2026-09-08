using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class TrackFilePublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "file_published",
                schema: "reporting",
                table: "artifacts",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "file_published",
                schema: "reporting",
                table: "artifacts"
            );
        }
    }
}
