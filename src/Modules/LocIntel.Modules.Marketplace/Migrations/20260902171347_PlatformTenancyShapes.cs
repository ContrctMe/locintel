using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocIntel.Modules.Marketplace.Migrations
{
    /// <summary>
    /// The pure two-party tables (request_events, request_recipients, quotes)
    /// move from hand-written policies onto the Platform shape
    /// (ITwoPartyScoped + EnableTwoPartyRls): same predicate, one reviewed
    /// place, adversarially tested upstream. vendor_profiles already carries
    /// exactly the policies EnablePublishedCatalogRls writes, so only its CLR
    /// shape changed. requests keeps its hand-written policy on purpose - a
    /// broadcast request is readable by every recipient, a third party the
    /// two-party shape cannot express.
    /// </summary>
    public partial class PlatformTenancyShapes : Migration
    {
        private static readonly string[] TwoPartyTables =
        [
            "request_events",
            "request_recipients",
            "quotes",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TwoPartyTables)
            {
                migrationBuilder.Sql($"""DROP POLICY two_party ON "marketplace"."{table}";""");
                migrationBuilder.EnableTwoPartyRls("marketplace", table, "vendor_org_id");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TwoPartyTables)
                migrationBuilder.Sql(
                    $"""
                    DROP POLICY tenant_isolation ON "marketplace"."{table}";
                    CREATE POLICY two_party ON "marketplace"."{table}" FOR ALL
                        USING (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                               OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                        WITH CHECK (org_id = NULLIF(current_setting('app.org_id', true), '')::uuid
                               OR vendor_org_id = NULLIF(current_setting('app.org_id', true), '')::uuid);
                    """
                );
        }
    }
}
