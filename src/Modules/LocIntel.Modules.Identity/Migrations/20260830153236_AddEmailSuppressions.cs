using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSuppressions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PLATFORM-GLOBAL table (no org_id, no RLS policy - the skill's
            // documented exception): an undeliverable address is undeliverable
            // for every org, and bounce reports arrive before tenant context.
            migrationBuilder.CreateTable(
                name: "email_suppressions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(
                        type: "character varying(320)",
                        maxLength: 320,
                        nullable: false
                    ),
                    reason = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_suppressions", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_email_suppressions_email",
                schema: "identity",
                table: "email_suppressions",
                column: "email",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "email_suppressions", schema: "identity");
        }
    }
}
