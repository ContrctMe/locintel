using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class DirectoryStatusPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_platform",
                schema: "identity",
                table: "org_directory",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "identity",
                table: "org_directory",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: ""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_platform",
                schema: "identity",
                table: "org_directory"
            );

            migrationBuilder.DropColumn(name: "status", schema: "identity", table: "org_directory");
        }
    }
}
