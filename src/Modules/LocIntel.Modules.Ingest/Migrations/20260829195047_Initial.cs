using System;
using Microsoft.EntityFrameworkCore.Migrations;
using LocIntel.Platform.Data;

#nullable disable

namespace LocIntel.Modules.Ingest.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "ingest");

            migrationBuilder.CreateTable(
                name: "import_batches",
                schema: "ingest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    counts = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "site_connectors",
                schema: "ingest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    type = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    url = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    encrypted_credentials = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    last_synced_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_connectors", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "staged_sites",
                schema: "ingest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    time_zone = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    node_path = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_status = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    action = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    errors = table.Column<string[]>(type: "text[]", nullable: false),
                    changes = table.Column<string[]>(type: "text[]", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staged_sites", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_org_id_created_at",
                schema: "ingest",
                table: "import_batches",
                columns: new[] { "org_id", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_site_connectors_org_id_name",
                schema: "ingest",
                table: "site_connectors",
                columns: new[] { "org_id", "name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_staged_sites_batch_id",
                schema: "ingest",
                table: "staged_sites",
                column: "batch_id"
            );

            // RLS (new-migration skill checklist)
            migrationBuilder.EnableTenantRls("ingest", "import_batches");
            migrationBuilder.EnableTenantRls("ingest", "staged_sites");
            migrationBuilder.EnableTenantRls("ingest", "site_connectors");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "import_batches", schema: "ingest");

            migrationBuilder.DropTable(name: "site_connectors", schema: "ingest");

            migrationBuilder.DropTable(name: "staged_sites", schema: "ingest");
        }
    }
}
