using Microsoft.EntityFrameworkCore.Migrations;

namespace LocIntel.Platform.Data;

/// <summary>
/// RLS policy helper for migrations. EVERY org-scoped table calls this in the
/// migration that creates it (see the new-migration skill); RlsCoverageTests
/// asserts coverage against the live catalog.
///
/// This is the ONLY tenancy shape (ADR 48): every row has exactly one owning
/// org. When another org needs to see or act on a row, it gets its own row,
/// materialized through the outbox - never a counterparty column, a
/// recipients side table, or a published flag on this one. The shapes that
/// tried that were removed; ADR 48 records why and how a fork remodels them.
///
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
        migrationBuilder.Sql(TenantSql(schema, table, orgColumn));
    }

    // The policy text as a pure function, so EnableTenantRls stays a one-liner
    // and the SQL is reviewable in one place.
    internal static string TenantSql(string schema, string table, string orgColumn = "org_id") =>
        $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid)
                WITH CHECK ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid);
            """;
}
