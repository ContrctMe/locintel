using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Incidents.Migrations
{
    /// <inheritdoc />
    public partial class LocalTimeStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "local_hour",
                schema: "incidents",
                table: "incidents",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "local_weekday",
                schema: "incidents",
                table: "incidents",
                type: "integer",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "local_hour",
                schema: "incidents",
                table: "incidents"
            );

            migrationBuilder.DropColumn(
                name: "local_weekday",
                schema: "incidents",
                table: "incidents"
            );
        }
    }
}
