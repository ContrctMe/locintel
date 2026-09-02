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
}
