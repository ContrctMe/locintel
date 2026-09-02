using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                schema: "tenancy",
                table: "organizations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_organizations_external_id",
                schema: "tenancy",
                table: "organizations",
                column: "external_id",
                unique: true,
                filter: "external_id IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_organizations_external_id",
                schema: "tenancy",
                table: "organizations"
            );

            migrationBuilder.DropColumn(
                name: "external_id",
                schema: "tenancy",
                table: "organizations"
            );
        }
    }
}
