using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Incidents.Migrations
{
    /// <inheritdoc />
    public partial class TipIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reporter_contact",
                schema: "incidents",
                table: "incidents",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reporter_contact",
                schema: "incidents",
                table: "incidents"
            );
        }
    }
}
