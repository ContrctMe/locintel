using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Marketplace.Migrations
{
    /// <inheritdoc />
    public partial class Rfq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "vendor_org_id",
                schema: "marketplace",
                table: "requests",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid"
            );

            migrationBuilder.AddColumn<string>(
                name: "mode",
                schema: "marketplace",
                table: "requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "vendor_org_id",
                schema: "marketplace",
                table: "request_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid"
            );

            migrationBuilder.CreateTable(
                name: "quotes",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(
                        type: "numeric(14,2)",
                        precision: 14,
                        scale: 2,
                        nullable: false
                    ),
                    currency = table.Column<string>(
                        type: "character varying(3)",
                        maxLength: 3,
                        nullable: false
                    ),
                    notes = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    valid_until = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotes", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "request_recipients",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    notified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    responded_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_recipients", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_quotes_request_id_vendor_org_id",
                schema: "marketplace",
                table: "quotes",
                columns: new[] { "request_id", "vendor_org_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_recipients_request_id_vendor_org_id",
                schema: "marketplace",
                table: "request_recipients",
                columns: new[] { "request_id", "vendor_org_id" },
                unique: true
            );
            // RLS: the new two-party tables, and requests now also admit the
            // vendors a broadcast was sent to (mirrors the context's filter)
            migrationBuilder.Sql(
                """
                ALTER TABLE "marketplace"."request_recipients" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "marketplace"."request_recipients" FORCE ROW LEVEL SECURITY;
                CREATE POLICY two_party ON "marketplace"."request_recipients" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);

                ALTER TABLE "marketplace"."quotes" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "marketplace"."quotes" FORCE ROW LEVEL SECURITY;
                CREATE POLICY two_party ON "marketplace"."quotes" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);

                DROP POLICY two_party ON "marketplace"."requests";
                CREATE POLICY two_party ON "marketplace"."requests" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR EXISTS (SELECT 1 FROM "marketplace"."request_recipients" x
                                      WHERE x.request_id = "requests".id AND x.vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid))
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY two_party ON "marketplace"."requests";
                CREATE POLICY two_party ON "marketplace"."requests" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                """
            );
            migrationBuilder.DropTable(name: "quotes", schema: "marketplace");

            migrationBuilder.DropTable(name: "request_recipients", schema: "marketplace");

            migrationBuilder.DropColumn(name: "mode", schema: "marketplace", table: "requests");

            migrationBuilder.AlterColumn<Guid>(
                name: "vendor_org_id",
                schema: "marketplace",
                table: "requests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "vendor_org_id",
                schema: "marketplace",
                table: "request_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true
            );
        }
    }
}
