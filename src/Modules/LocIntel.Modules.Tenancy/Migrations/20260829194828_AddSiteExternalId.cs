using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                schema: "tenancy",
                table: "sites",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_sites_org_id_external_id",
                schema: "tenancy",
                table: "sites",
                columns: new[] { "org_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sites_org_id_external_id",
                schema: "tenancy",
                table: "sites"
            );

            migrationBuilder.DropColumn(name: "external_id", schema: "tenancy", table: "sites");
        }
    }
}
