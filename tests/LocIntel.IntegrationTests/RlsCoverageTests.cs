using System.Text.RegularExpressions;
using LocIntel.Api;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The crown-jewel gate (security review): CLAUDE.md promises "new
/// tenant-scoped tables need an RLS policy in the same migration; CI asserts
/// coverage" - this is that assertion.
///
/// Which tables are org-scoped comes from two sources, unioned: every
/// IOrgScoped entity in every module's EF model, with the column OrgId is
/// mapped to (so a table whose column is owner_org_id is seen - a fork had
/// two such tables invisible to a check keyed on the literal name), and every
/// table with a column literally named org_id (so a raw-SQL table with no
/// entity is seen too). Each MUST have row security ENABLED, FORCED (so even
/// a table-owning role obeys it), and a policy that names that column -
/// UNLESS the module's catalog entry declares it PlatformGlobal with a
/// reason: resolved before any tenant context exists and protected by
/// explicit query filters instead. That declaration used to be an
/// allow-list in this file, which put a security decision in an upstream
/// test every fork had to edit.
/// </summary>
public class RlsCoverageTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static HashSet<string> PlatformGlobal =>
        ModuleCatalog
            .AllWithPlatform.SelectMany(m => m.PlatformGlobal.Select(t => $"{m.Schema}.{t.Table}"))
            .ToHashSet();

    [Fact]
    public async Task Every_org_scoped_table_has_forced_rls_and_a_policy_on_its_org_column()
    {
        var expected = await OrgScopedTablesAsync();
        Assert.NotEmpty(expected);

        await using var conn = new NpgsqlConnection(fixture.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT n.nspname || '.' || c.relname AS name,
                   c.relrowsecurity, c.relforcerowsecurity,
                   (SELECT count(*) FROM pg_policies p
                    WHERE p.schemaname = n.nspname AND p.tablename = c.relname) AS policies,
                   (SELECT string_agg(coalesce(p.qual, '') || ' ' || coalesce(p.with_check, ''), ' ')
                    FROM pg_policies p
                    WHERE p.schemaname = n.nspname AND p.tablename = c.relname) AS predicates
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND n.nspname = ANY(@schemas)
            ORDER BY name
            """,
            conn
        );
        cmd.Parameters.AddWithValue("schemas", ModuleCatalog.Schemas.ToArray());
        var offenders = new List<string>();
        var seen = new HashSet<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (!expected.TryGetValue(name, out var column))
                continue;
            seen.Add(name);
            if (PlatformGlobal.Contains(name))
                continue;
            var (rls, force, policies) = (
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetInt32(3)
            );
            var predicates = reader.IsDBNull(4) ? "" : reader.GetString(4);
            // the deparsed predicate spells a simple lowercase identifier bare
            var onColumn = Regex.IsMatch(predicates, $@"(?<![\w.]){Regex.Escape(column)}\b");
            if (!rls || !force || policies == 0 || !onColumn)
                offenders.Add(
                    $"{name} (rls={rls}, force={force}, policies={policies}, on {column}={onColumn})"
                );
        }
        var unmapped = expected.Keys.Except(seen).Order().ToArray();
        Assert.True(
            unmapped.Length == 0,
            "IOrgScoped entities whose table does not exist - the model and the migrations disagree: "
                + string.Join(", ", unmapped)
        );
        Assert.True(
            offenders.Count == 0,
            "org-scoped tables missing forced RLS + a policy on their org column (add "
                + "EnableTenantRls(schema, table, orgColumn) in the migration, or - if truly "
                + "resolved pre-tenant - declare it PlatformGlobal on the module's catalog entry "
                + "with a reason): "
                + string.Join("; ", offenders)
        );
    }

    [Fact]
    public async Task Platform_global_declarations_stay_honest()
    {
        // the inverse guard: a declared table that is not org-scoped (renamed,
        // dropped, or never was) is a dead declaration - keeps the exceptions
        // from rotting into a place to hide a real miss
        var expected = await OrgScopedTablesAsync();
        var stale = PlatformGlobal.Where(t => !expected.ContainsKey(t)).Order().ToArray();
        Assert.True(
            stale.Length == 0,
            "PlatformGlobal declarations naming no org-scoped table: " + string.Join(", ", stale)
        );
    }

    /// <summary>schema.table -> the column that holds the owning org.</summary>
    private async Task<Dictionary<string, string>> OrgScopedTablesAsync()
    {
        var tables = new Dictionary<string, string>();
        foreach (var module in ModuleCatalog.AllWithPlatform)
        {
            await using var db = ApiFixture.CreateCatalogContext(
                module,
                fixture.PostgresConnectionString
            );
            foreach (var entity in db.Model.GetEntityTypes())
            {
                if (!typeof(IOrgScoped).IsAssignableFrom(entity.ClrType))
                    continue;
                var table = entity.GetTableName();
                if (table is null)
                    continue;
                var schema = entity.GetSchema() ?? module.Schema;
                var column = entity
                    .FindProperty(nameof(IOrgScoped.OrgId))!
                    .GetColumnName(StoreObjectIdentifier.Table(table, schema));
                if (column is not null)
                    tables[$"{schema}.{table}"] = column;
            }
        }

        await using var conn = new NpgsqlConnection(fixture.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT table_schema || '.' || table_name FROM information_schema.columns
            WHERE column_name = 'org_id' AND table_schema = ANY(@schemas)
            """,
            conn
        );
        cmd.Parameters.AddWithValue("schemas", ModuleCatalog.Schemas.ToArray());
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.TryAdd(reader.GetString(0), "org_id");
        return tables;
    }
}
