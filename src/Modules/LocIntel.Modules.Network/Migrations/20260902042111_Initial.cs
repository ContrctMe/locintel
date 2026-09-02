using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Network.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "network");

            migrationBuilder.CreateTable(
                name: "share_access",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    role = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: true),
                    joined_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_share_access", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "share_members",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    role = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    joined_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_share_members", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "shared_bulletins",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    severity = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    body = table.Column<string>(type: "text", nullable: false),
                    entity_kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    display_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    descriptors = table.Column<string>(type: "jsonb", nullable: false),
                    areas = table.Column<string[]>(type: "text[]", nullable: false),
                    source_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    withdrawn_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_bulletins", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "shares",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    description = table.Column<string>(type: "text", nullable: false),
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
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shares", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_share_access_share_id_org_id",
                schema: "network",
                table: "share_access",
                columns: new[] { "share_id", "org_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_share_members_share_id_org_id",
                schema: "network",
                table: "share_members",
                columns: new[] { "share_id", "org_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_shared_bulletins_share_id_published_at",
                schema: "network",
                table: "shared_bulletins",
                columns: new[] { "share_id", "published_at" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_shares_owner_org_id",
                schema: "network",
                table: "shares",
                column: "owner_org_id"
            );
            // RLS (the new-migration skill checklist). share_access is the single
            // owner row; every other policy is ONE hop through it (never through
            // its own table), mirroring the context's "Tenant" filters.
            migrationBuilder.EnableTenantRls("network", "share_access");
            migrationBuilder.Sql(
                """
                ALTER TABLE "network"."shares" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "network"."shares" FORCE ROW LEVEL SECURITY;
                CREATE POLICY member_read ON "network"."shares" FOR SELECT
                    USING (EXISTS (SELECT 1 FROM "network"."share_access" a
                                   WHERE a.share_id = "shares".id
                                     AND a.org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                                     AND a.status <> 'Removed'));
                CREATE POLICY owner_write ON "network"."shares" FOR ALL
                    USING (owner_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (owner_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);

                ALTER TABLE "network"."share_members" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "network"."share_members" FORCE ROW LEVEL SECURITY;
                CREATE POLICY member_read ON "network"."share_members" FOR SELECT
                    USING (EXISTS (SELECT 1 FROM "network"."share_access" a
                                   WHERE a.share_id = "share_members".share_id
                                     AND a.org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                                     AND a.status <> 'Removed'));
                CREATE POLICY self_or_owner_write ON "network"."share_members" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR EXISTS (SELECT 1 FROM "network"."shares" s
                                      WHERE s.id = "share_members".share_id
                                        AND s.owner_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid))
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR EXISTS (SELECT 1 FROM "network"."shares" s
                                      WHERE s.id = "share_members".share_id
                                        AND s.owner_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid));

                ALTER TABLE "network"."shared_bulletins" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "network"."shared_bulletins" FORCE ROW LEVEL SECURITY;
                CREATE POLICY member_read ON "network"."shared_bulletins" FOR SELECT
                    USING (publisher_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR EXISTS (SELECT 1 FROM "network"."share_access" a
                                      WHERE a.share_id = "shared_bulletins".share_id
                                        AND a.org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                                        AND a.status = 'Active'));
                CREATE POLICY publisher_write ON "network"."shared_bulletins" FOR ALL
                    USING (publisher_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (publisher_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // policies on other tables reference share_access: drop them before
            // the tables go (Down is maintained, not decorative - ADR 38)
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS member_read ON "network"."shares";
                DROP POLICY IF EXISTS member_read ON "network"."share_members";
                DROP POLICY IF EXISTS self_or_owner_write ON "network"."share_members";
                DROP POLICY IF EXISTS member_read ON "network"."shared_bulletins";
                """
            );
            migrationBuilder.DropTable(name: "share_access", schema: "network");

            migrationBuilder.DropTable(name: "share_members", schema: "network");

            migrationBuilder.DropTable(name: "shared_bulletins", schema: "network");

            migrationBuilder.DropTable(name: "shares", schema: "network");
        }
    }
}
