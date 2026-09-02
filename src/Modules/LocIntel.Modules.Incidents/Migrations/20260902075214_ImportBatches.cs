using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Incidents.Migrations
{
    /// <inheritdoc />
    public partial class ImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_batches",
                schema: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    committed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    total = table.Column<int>(type: "integer", nullable: false),
                    valid = table.Column<int>(type: "integer", nullable: false),
                    invalid = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "import_rows",
                schema: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    site_ref = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: true
                    ),
                    severity = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    narrative = table.Column<string>(type: "text", nullable: false),
                    loss_amount = table.Column<decimal>(
                        type: "numeric(14,2)",
                        precision: 14,
                        scale: 2,
                        nullable: true
                    ),
                    police_report_number = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    errors = table.Column<string[]>(type: "text[]", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_rows", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_org_id_created_at",
                schema: "incidents",
                table: "import_batches",
                columns: new[] { "org_id", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_import_rows_batch_id_row_number",
                schema: "incidents",
                table: "import_rows",
                columns: new[] { "batch_id", "row_number" }
            );
            // RLS (the new-migration skill checklist): every org-scoped table
            migrationBuilder.EnableTenantRls("incidents", "import_batches");
            migrationBuilder.EnableTenantRls("incidents", "import_rows");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "import_batches", schema: "incidents");

            migrationBuilder.DropTable(name: "import_rows", schema: "incidents");
        }
    }
}
