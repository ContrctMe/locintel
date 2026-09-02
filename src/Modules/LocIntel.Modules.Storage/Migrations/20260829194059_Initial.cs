using System;
using Microsoft.EntityFrameworkCore.Migrations;
using LocIntel.Platform.Data;

#nullable disable

namespace LocIntel.Modules.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "storage");

            migrationBuilder.CreateTable(
                name: "files",
                schema: "storage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: false
                    ),
                    content_type = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    max_bytes = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    legal_hold = table.Column<bool>(type: "boolean", nullable: false),
                    preview_key = table.Column<string>(
                        type: "character varying(520)",
                        maxLength: 520,
                        nullable: true
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    scanned_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_files_org_id_created_at",
                schema: "storage",
                table: "files",
                columns: new[] { "org_id", "created_at" }
            );

            // RLS (new-migration skill checklist)
            migrationBuilder.EnableTenantRls("storage", "files");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "files", schema: "storage");
        }
    }
}
