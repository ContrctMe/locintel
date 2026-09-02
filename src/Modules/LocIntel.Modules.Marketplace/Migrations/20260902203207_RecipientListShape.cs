using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Marketplace.Migrations
{
    /// <summary>
    /// requests moves from its hand-written policy onto the Platform
    /// recipient-list shape: the same predicate (owner or awarded vendor may
    /// write; every broadcast recipient may read), now written by
    /// EnableRecipientListRls and tested adversarially upstream. No column
    /// changes: ServiceRequest.CounterpartyOrgId still maps to vendor_org_id.
    /// </summary>
    public partial class RecipientListShape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY two_party ON "marketplace"."requests";""");
            migrationBuilder.EnableRecipientListRls(
                "marketplace",
                "requests",
                recipientsTable: "request_recipients",
                foreignKeyColumn: "request_id",
                recipientOrgColumn: "vendor_org_id",
                counterpartyColumn: "vendor_org_id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY tenant_isolation ON "marketplace"."requests";
                CREATE POLICY two_party ON "marketplace"."requests" FOR ALL
                    USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                           OR EXISTS (SELECT 1 FROM "marketplace"."request_recipients" x
                                      WHERE x.request_id = "requests".id AND x.vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid))
                    WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                """
            );
        }
    }
}
