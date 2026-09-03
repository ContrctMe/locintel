using System;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Network.Migrations
{
    /// <summary>
    /// ADR 48: the owner keeps the share and its roster; each member's access
    /// row becomes its projection of the share (owner, description, status,
    /// roster); bulletins stay with their publisher and every active member
    /// gets its own copy. The one-hop cross-tenant policies go.
    /// </summary>
    public partial class OneOwnerPerRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_share_members_share_id_org_id",
                schema: "network",
                table: "share_members"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "member_org_id",
                schema: "network",
                table: "share_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "network",
                table: "share_access",
                type: "text",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "owner_name",
                schema: "network",
                table: "share_access",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<Guid>(
                name: "owner_org_id",
                schema: "network",
                table: "share_access",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.AddColumn<string>(
                name: "roster",
                schema: "network",
                table: "share_access",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]"
            );

            migrationBuilder.AddColumn<string>(
                name: "share_status",
                schema: "network",
                table: "share_access",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active"
            );
            migrationBuilder.Sql(
                """
                -- the roster is the owner's: org_id becomes the owner, member_org_id the member
                UPDATE "network"."share_members" m SET member_org_id = m.org_id, org_id = s.owner_org_id
                FROM "network"."shares" s WHERE s.id = m.share_id;
                -- each access row becomes that org's projection of the share
                UPDATE "network"."share_access" a
                SET owner_org_id = s.owner_org_id, owner_name = s.owner_name, description = s.description, share_status = s.status,
                    roster = COALESCE((SELECT json_agg(json_build_object('OrgId', m.member_org_id, 'OrgName', m.org_name, 'Role', m.role,
                                                                         'Status', m.status, 'JoinedAt', m.joined_at)
                                                       ORDER BY m.created_at)
                                       FROM "network"."share_members" m WHERE m.share_id = a.share_id), '[]'::json)::jsonb
                FROM "network"."shares" s WHERE s.id = a.share_id;
                """
            );

            migrationBuilder.CreateTable(
                name: "shared_bulletin_copies",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bulletin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
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
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_bulletin_copies", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_share_members_share_id_member_org_id",
                schema: "network",
                table: "share_members",
                columns: new[] { "share_id", "member_org_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_shared_bulletin_copies_org_id_bulletin_id",
                schema: "network",
                table: "shared_bulletin_copies",
                columns: new[] { "org_id", "bulletin_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_shared_bulletin_copies_share_id_published_at",
                schema: "network",
                table: "shared_bulletin_copies",
                columns: new[] { "share_id", "published_at" }
            );
            migrationBuilder.Sql(
                """
                -- every active member's own copy of what others published into the share
                INSERT INTO "network"."shared_bulletin_copies"
                    (id, org_id, bulletin_id, share_id, share_name, publisher_org_id, publisher_name, kind, severity, title, body,
                     entity_kind, display_name, aliases, descriptors, areas, published_at, expires_at, withdrawn_at, created_at)
                SELECT gen_random_uuid(), a.org_id, b.id, b.share_id, a.share_name, b.publisher_org_id, b.publisher_name, b.kind,
                       b.severity, b.title, b.body, b.entity_kind, b.display_name, b.aliases, b.descriptors, b.areas, b.published_at,
                       b.expires_at, b.withdrawn_at, now()
                FROM "network"."shared_bulletins" b
                JOIN "network"."share_access" a ON a.share_id = b.share_id AND a.status = 'Active' AND a.org_id <> b.publisher_org_id;
                DROP POLICY tenant_isolation ON "network"."shares";
                DROP POLICY member_read ON "network"."share_members";
                DROP POLICY self_or_owner_write ON "network"."share_members";
                DROP POLICY member_read ON "network"."shared_bulletins";
                DROP POLICY publisher_write ON "network"."shared_bulletins";
                """
            );
            migrationBuilder.EnableTenantRls("network", "shares", orgColumn: "owner_org_id");
            migrationBuilder.EnableTenantRls("network", "share_members");
            migrationBuilder.EnableTenantRls(
                "network",
                "shared_bulletins",
                orgColumn: "publisher_org_id"
            );
            migrationBuilder.EnableTenantRls("network", "shared_bulletin_copies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "network"."share_members" SET org_id = member_org_id;
                DROP POLICY tenant_isolation ON "network"."shares";
                DROP POLICY tenant_isolation ON "network"."share_members";
                DROP POLICY tenant_isolation ON "network"."shared_bulletins";
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
            migrationBuilder.Sql(
                """
                ALTER TABLE "network"."shares" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "network"."shares" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "network"."shares"
                    USING ("owner_org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR EXISTS (SELECT 1 FROM "network"."share_access" r
                                      WHERE r."share_id" = "shares"."id" AND r."org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid AND (r.status <> 'Removed')))
                    WITH CHECK ("owner_org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid);
                """
            );
            migrationBuilder.DropTable(name: "shared_bulletin_copies", schema: "network");

            migrationBuilder.DropIndex(
                name: "IX_share_members_share_id_member_org_id",
                schema: "network",
                table: "share_members"
            );

            migrationBuilder.DropColumn(
                name: "member_org_id",
                schema: "network",
                table: "share_members"
            );

            migrationBuilder.DropColumn(
                name: "description",
                schema: "network",
                table: "share_access"
            );

            migrationBuilder.DropColumn(
                name: "owner_name",
                schema: "network",
                table: "share_access"
            );

            migrationBuilder.DropColumn(
                name: "owner_org_id",
                schema: "network",
                table: "share_access"
            );

            migrationBuilder.DropColumn(name: "roster", schema: "network", table: "share_access");

            migrationBuilder.DropColumn(
                name: "share_status",
                schema: "network",
                table: "share_access"
            );

            migrationBuilder.CreateIndex(
                name: "IX_share_members_share_id_org_id",
                schema: "network",
                table: "share_members",
                columns: new[] { "share_id", "org_id" },
                unique: true
            );
        }
    }
}
