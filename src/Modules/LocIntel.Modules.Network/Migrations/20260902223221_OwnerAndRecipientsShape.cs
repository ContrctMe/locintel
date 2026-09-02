using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Network.Migrations
{
    /// <summary>
    /// shares moves from its hand-written member_read + owner_write pair onto
    /// the Platform recipient-list shape, gated on the share_access row's
    /// status so a removed member (kept for the trail) loses access. Same
    /// predicate as before, one reviewed place. share_members and
    /// shared_bulletins stay hand-written: they join share_access on the
    /// parent's share_id, not its id, which the helper cannot express yet.
    /// </summary>
    public partial class OwnerAndRecipientsShape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY member_read ON "network"."shares";
                DROP POLICY owner_write ON "network"."shares";
                """
            );
            migrationBuilder.EnableRecipientListRls(
                "network",
                "shares",
                recipientsTable: "share_access",
                foreignKeyColumn: "share_id",
                recipientOrgColumn: "org_id",
                orgColumn: "owner_org_id",
                recipientPredicate: "r.status <> 'Removed'"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY tenant_isolation ON "network"."shares";
                CREATE POLICY member_read ON "network"."shares" FOR SELECT
                    USING (EXISTS (SELECT 1 FROM "network"."share_access" a
                                   WHERE a.share_id = "shares".id
                                     AND a.org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                                     AND a.status <> 'Removed'));
                CREATE POLICY owner_write ON "network"."shares" FOR ALL
                    USING (owner_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                    WITH CHECK (owner_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                """
            );
        }
    }
}
