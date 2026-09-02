using System;
using Microsoft.EntityFrameworkCore.Migrations;
using LocIntel.Platform.Data;

#nullable disable

namespace LocIntel.Platform.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "platform");

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "platform",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    endpoint = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: false
                    ),
                    request_hash = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    content_type = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    body = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.org_id, x.key });
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_created_at",
                schema: "platform",
                table: "idempotency_keys",
                column: "created_at"
            );

            // RLS (new-migration skill checklist), plus a dedicated policy so
            // the cleanup job may hard-delete EXPIRED rows across orgs.
            migrationBuilder.EnableTenantRls("platform", "idempotency_keys");
            migrationBuilder.Sql(
                """
                CREATE POLICY expired_cleanup ON "platform"."idempotency_keys"
                    FOR DELETE USING (created_at < now() - interval '24 hours');
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "idempotency_keys", schema: "platform");
        }
    }
}
