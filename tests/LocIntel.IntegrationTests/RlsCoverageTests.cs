using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The crown-jewel gate (security review): CLAUDE.md promises "new
/// tenant-scoped tables need an RLS policy in the same migration; CI asserts
/// coverage" - this is that assertion, which did not previously exist.
///
/// Every table carrying an org_id in a module schema MUST have row security
/// ENABLED, FORCED (so even a table-owning role obeys it), and a policy -
/// UNLESS it is one of the deliberately platform-global tables, which are
/// resolved BEFORE any tenant context exists (credential lookup, membership
/// resolution, the org read model) and are protected by explicit query
/// filters instead. A new org_id table that forgets EnableTenantRls, or a
/// new platform-global table added without conscious thought, fails here.
/// </summary>
public class RlsCoverageTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // resolved before tenant context (ADR 7/37): NOT RLS'd BY DESIGN.
    // Adding to this set is a deliberate security decision, not a shortcut.
    private static readonly HashSet<string> PlatformGlobal =
    [
        "identity.api_keys", // looked up by unguessable secret hash
        "identity.memberships", // filtered by the authenticated user id
        "identity.org_directory", // the pre-tenant org read model
        "marketplace.vendor_directory", // public projection of published vendors (ADR 48 open pull); holds nothing a vendor keeps private
    ];

    [Fact]
    public async Task Every_org_scoped_table_has_forced_rls_and_a_policy()
    {
        await using var conn = new NpgsqlConnection(fixture.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT n.nspname || '.' || c.relname AS name,
                   c.relrowsecurity, c.relforcerowsecurity,
                   (SELECT count(*) FROM pg_policies p
                    WHERE p.schemaname = n.nspname AND p.tablename = c.relname) AS policies
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p')
              AND n.nspname = ANY(@schemas)
              AND EXISTS (
                  SELECT 1 FROM information_schema.columns col
                  WHERE col.table_schema = n.nspname AND col.table_name = c.relname
                    AND col.column_name = 'org_id')
            ORDER BY name
            """,
            conn
        );
        cmd.Parameters.AddWithValue("schemas", LocIntel.Api.ModuleCatalog.Schemas.ToArray());
        var offenders = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (PlatformGlobal.Contains(name))
                continue;
            var (rls, force, policies) = (
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetInt32(3)
            );
            if (!rls || !force || policies == 0)
                offenders.Add($"{name} (rls={rls}, force={force}, policies={policies})");
        }
        Assert.True(
            offenders.Count == 0,
            "org-scoped tables missing forced RLS + a policy (add EnableTenantRls in the "
                + "migration, or - if truly resolved pre-tenant - add to PlatformGlobal with a reason): "
                + string.Join("; ", offenders)
        );
    }

    [Fact]
    public async Task Platform_global_allowlist_stays_honest()
    {
        // the inverse guard: if a table LEAVES the org_id set (renamed/dropped),
        // its allowlist entry is dead and should be removed - keeps the
        // exceptions list from rotting into a place to hide a real miss.
        await using var conn = new NpgsqlConnection(fixture.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT n.nspname || '.' || c.relname
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r','p')
              AND EXISTS (SELECT 1 FROM information_schema.columns col
                          WHERE col.table_schema = n.nspname AND col.table_name = c.relname
                            AND col.column_name = 'org_id')
            """,
            conn
        );
        var withOrgId = new HashSet<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            withOrgId.Add(reader.GetString(0));
        var stale = PlatformGlobal.Where(t => !withOrgId.Contains(t)).ToArray();
        Assert.True(stale.Length == 0, "dead PlatformGlobal entries: " + string.Join(", ", stale));
    }
}
