using Microsoft.EntityFrameworkCore.Migrations;

namespace LocIntel.Platform.Data;

/// <summary>
/// RLS policy helper for migrations. EVERY org-scoped table calls this in the
/// migration that creates it (see the new-migration skill); CI asserts coverage.
/// NULLIF guards a pooled-connection quirk: after DISCARD ALL a custom GUC
/// reads as '' (not NULL), and ''::uuid errors - NULLIF turns it back into
/// NULL so an unset tenant matches nothing instead of erroring.
/// FORCE applies the policy to the table owner too, so even a misconfigured
/// app role cannot bypass it.
/// </summary>
public static class RlsMigrationExtensions
{
    public static void EnableTenantRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string orgColumn = "org_id"
    )
    {
        migrationBuilder.Sql(
            $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid)
                WITH CHECK ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid);
            """
        );
    }

    /// <summary>
    /// A row two orgs share (<see cref="Kernel.ITwoPartyScoped"/>): either
    /// side sees it. One policy, FOR ALL, so a counterparty can advance the
    /// row it is party to. WITH CHECK repeats the predicate: without it, a
    /// tenant could INSERT a row naming two OTHER orgs.
    /// </summary>
    public static void EnableTwoPartyRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string counterpartyColumn,
        string orgColumn = "org_id"
    )
    {
        migrationBuilder.Sql(TwoPartySql(schema, table, counterpartyColumn, orgColumn));
    }

    /// <summary>
    /// Owner-writes, everyone-reads-once-published
    /// (<see cref="Kernel.IPublishedCatalogScoped"/>). TWO policies on
    /// purpose: a single FOR ALL policy that allowed published reads would
    /// also allow published WRITES by any tenant.
    /// </summary>
    public static void EnablePublishedCatalogRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string publishedColumn = "published",
        string orgColumn = "org_id"
    )
    {
        migrationBuilder.Sql(CatalogSql(schema, table, publishedColumn, orgColumn));
    }

    // SQL as pure functions so the policies can be asserted directly in tests
    // rather than only through a module that happens to use them.
    internal static string TwoPartySql(
        string schema,
        string table,
        string counterpartyColumn,
        string orgColumn = "org_id"
    ) =>
        $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING (
                    "{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                    OR "{counterpartyColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                )
                WITH CHECK (
                    "{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                    OR "{counterpartyColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                );
            """;

    internal static string CatalogSql(
        string schema,
        string table,
        string publishedColumn = "published",
        string orgColumn = "org_id"
    ) =>
        $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY catalog_read ON "{schema}"."{table}" FOR SELECT
                USING (
                    "{publishedColumn}"
                    OR "{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                );
            CREATE POLICY owner_write ON "{schema}"."{table}" FOR ALL
                USING ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid)
                WITH CHECK ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid);
            """;
}
