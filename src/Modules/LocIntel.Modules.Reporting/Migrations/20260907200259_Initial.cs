using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "reporting");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    report_type = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    selection = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    options_json = table.Column<string>(type: "jsonb", nullable: false),
                    site_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    state = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    error_code = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: true
                    ),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    lease_owner = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    completed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    metadata_expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.id);
                    table.UniqueConstraint("AK_jobs_org_id_id", x => new { x.org_id, x.id });
                }
            );

            migrationBuilder.CreateTable(
                name: "artifacts",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<string>(
                        type: "character varying(520)",
                        maxLength: 520,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    content_type = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    ready = table.Column<bool>(type: "boolean", nullable: false),
                    bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_artifacts_jobs_org_id_job_id",
                        columns: x => new { x.org_id, x.job_id },
                        principalSchema: "reporting",
                        principalTable: "jobs",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "items",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    state = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    error_code = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: true
                    ),
                    warnings_json = table.Column<string>(type: "jsonb", nullable: false),
                    dependencies_json = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_items_jobs_org_id_job_id",
                        columns: x => new { x.org_id, x.job_id },
                        principalSchema: "reporting",
                        principalTable: "jobs",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_key",
                schema: "reporting",
                table: "artifacts",
                column: "key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_org_id_job_id",
                schema: "reporting",
                table: "artifacts",
                columns: new[] { "org_id", "job_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_items_org_id_job_id",
                schema: "reporting",
                table: "items",
                columns: new[] { "org_id", "job_id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_jobs_org_id_metadata_expires_at",
                schema: "reporting",
                table: "jobs",
                columns: new[] { "org_id", "metadata_expires_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_jobs_org_id_requested_by_created_at",
                schema: "reporting",
                table: "jobs",
                columns: new[] { "org_id", "requested_by", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_jobs_org_id_state_lease_until",
                schema: "reporting",
                table: "jobs",
                columns: new[] { "org_id", "state", "lease_until" }
            );
            migrationBuilder.EnableTenantRls("reporting", "jobs");
            migrationBuilder.EnableTenantRls("reporting", "items");
            migrationBuilder.EnableTenantRls("reporting", "artifacts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "artifacts", schema: "reporting");

            migrationBuilder.DropTable(name: "items", schema: "reporting");

            migrationBuilder.DropTable(name: "jobs", schema: "reporting");
        }
    }
}
