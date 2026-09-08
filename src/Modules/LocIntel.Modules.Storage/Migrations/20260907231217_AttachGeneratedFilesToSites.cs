using System;
using Microsoft.EntityFrameworkCore.Migrations;
using LocIntel.Platform.Data;

#nullable disable

namespace LocIntel.Modules.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AttachGeneratedFilesToSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                schema: "storage",
                table: "files",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "origin_id",
                schema: "storage",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "site_ids",
                schema: "storage",
                table: "files",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.CreateTable(
                name: "purged_organizations",
                schema: "storage",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purged_organizations", x => x.org_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_files_site_ids",
                schema: "storage",
                table: "files",
                column: "site_ids")
                .Annotation("Npgsql:IndexMethod", "gin");
            migrationBuilder.EnableTenantRls("storage", "purged_organizations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purged_organizations",
                schema: "storage");

            migrationBuilder.DropIndex(
                name: "IX_files_site_ids",
                schema: "storage",
                table: "files");

            migrationBuilder.DropColumn(
                name: "origin",
                schema: "storage",
                table: "files");

            migrationBuilder.DropColumn(
                name: "origin_id",
                schema: "storage",
                table: "files");

            migrationBuilder.DropColumn(
                name: "site_ids",
                schema: "storage",
                table: "files");
        }
    }
}
