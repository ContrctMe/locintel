using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class SiteConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "tenancy",
                table: "sites",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "xmin", schema: "tenancy", table: "sites");
        }
    }
}
