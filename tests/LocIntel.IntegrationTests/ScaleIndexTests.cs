using System.Text.Json;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 51: the hot site predicates must reach an index AS THE APP ROLE.
/// Under row security Postgres refuses non-leakproof operators as index
/// conditions, so a plan that looks fine as the migrate role can be a
/// sequential scan in production; this asks the planner the way the API
/// does - app_user, app.org_id set - with sequential scans disabled, so an
/// unusable index shows up as "Seq Scan" instead of hiding behind a small
/// table. A couple of thousand sites are seeded first: on a ten-row table
/// the planner ties every index and the plan proves nothing.
/// </summary>
public class ScaleIndexTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const int Seeded = 2000;

    private async Task SeedAsync(HttpClient owner)
    {
        var rootId = await ApiFixture.EnsureRootAsync(owner);
        await using (
            var db = (TenancyDbContext)
                ApiFixture.CreateCatalogContext(
                    LocIntel.Api.ModuleCatalog.All.Single(module =>
                        module.DbContextType == typeof(TenancyDbContext)
                    ),
                    fixture.PostgresConnectionString
                )
        )
        {
            if (await db.Sites.IgnoreQueryFilters().CountAsync() >= Seeded)
                return;
            var node = await db
                .HierarchyNodes.IgnoreQueryFilters()
                .SingleAsync(n => n.Id == rootId);
            var words = new[]
            {
                "north",
                "harbor",
                "market",
                "ridge",
                "depot",
                "plaza",
                "mill",
                "bay",
            };
            for (var i = 0; i < Seeded; i++)
            {
                var id = SiteId.New();
                db.Sites.Add(
                    new Site
                    {
                        Id = id,
                        OrgId = fixture.OrgA,
                        NodeId = rootId,
                        Path = new LTree($"{node.Path}.{Site.Label(id)}"),
                        Name = $"{words[i % words.Length]} store {i}",
                        City = words[(i / 8) % words.Length],
                        TimeZone = "Etc/UTC",
                        Latitude = 26 + (i % 97) / 97.0 * 22,
                        Longitude = -124 + (i % 89) / 89.0 * 57,
                    }
                );
            }
            await db.SaveChangesAsync();
        }
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var analyze = connection.CreateCommand();
        analyze.CommandText = "ANALYZE tenancy.sites; ANALYZE tenancy.site_search_terms;";
        await analyze.ExecuteNonQueryAsync();
    }

    private async Task<string> ExplainAsAppUserAsync(
        string sql,
        params NpgsqlParameter[] parameters
    )
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();
        await using (var session = connection.CreateCommand())
        {
            session.CommandText =
                $"SELECT set_config('app.org_id', '{fixture.OrgA.Value}', false); SET enable_seqscan = off;";
            await session.ExecuteNonQueryAsync();
        }
        await using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN (FORMAT JSON) " + sql;
        explain.Parameters.AddRange(parameters);
        return (string)(await explain.ExecuteScalarAsync())!;
    }

    private static void AssertUsesIndex(string plan, string index, string keyColumn)
    {
        Assert.True(
            plan.Contains($"\"Index Name\": \"{index}\""),
            $"expected {index} in plan:\n{plan}"
        );
        // the key is an index CONDITION, not a filter over rows the index handed back
        var conditions = JsonDocument
            .Parse(plan)
            .RootElement.ToString()
            .Split('\n')
            .Where(line => line.Contains("\"Index Cond\""))
            .ToList();
        Assert.True(
            conditions.Any(c => c.Contains(keyColumn)),
            $"expected {keyColumn} in an Index Cond:\n{plan}"
        );
        var scans = JsonDocument
            .Parse(plan)
            .RootElement.EnumerateArray()
            .Select(p => p.GetProperty("Plan").ToString())
            .Single();
        Assert.DoesNotContain("\"Node Type\": \"Seq Scan\"", scans);
    }

    private static NpgsqlParameter Org(ApiFixture fixture) => new("org", fixture.OrgA.Value);

    [Fact]
    public async Task A_viewport_is_a_cell_range_on_the_btree()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await SeedAsync(owner);
        var (lo, hi) = LocIntel.Platform.Spatial.SpatialCells.TileRange(9, 81, 183);
        var plan = await ExplainAsAppUserAsync(
            """
            SELECT count(*) FROM tenancy.sites s
            WHERE s.org_id = @org AND s.cell >= @lo AND s.cell < @hi
              AND s.latitude >= 45 AND s.latitude <= 46 AND s.longitude >= -123 AND s.longitude <= -122
            """,
            Org(fixture),
            new NpgsqlParameter("lo", lo),
            new NpgsqlParameter("hi", hi)
        );
        AssertUsesIndex(plan, "IX_sites_org_id_cell", "cell");
    }

    [Fact]
    public async Task A_subtree_is_a_path_text_range_on_the_btree()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await SeedAsync(owner);
        var plan = await ExplainAsAppUserAsync(
            """
            SELECT count(*) FROM tenancy.sites s
            WHERE s.org_id = @org AND (s.path_text = @p OR (s.path_text >= @lo AND s.path_text < @hi))
            """,
            Org(fixture),
            new NpgsqlParameter("p", "nabc"),
            new NpgsqlParameter("lo", "nabc."),
            new NpgsqlParameter("hi", "nabc/")
        );
        AssertUsesIndex(plan, "IX_sites_org_id_path_text", "path_text");
    }

    [Fact]
    public async Task A_search_page_is_one_ordered_walk_of_the_term_index()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await SeedAsync(owner);
        var plan = await ExplainAsAppUserAsync(
            """
            SELECT s.id FROM tenancy.site_search_terms t
            JOIN tenancy.sites s ON s.id = t.site_id AND s.org_id = @org
            WHERE t.org_id = @org AND t.term >= @lo AND t.term < @hi
            ORDER BY t.term, t.name, t.site_id LIMIT 51
            """,
            Org(fixture),
            new NpgsqlParameter("lo", "dep"),
            new NpgsqlParameter("hi", "dep" + char.ConvertFromUtf32(0x10FFFF))
        );
        AssertUsesIndex(plan, "IX_site_search_terms_org_id_term_name_site_id", "term");
        // the index's own order is the page's order: nothing to sort
        Assert.DoesNotContain("\"Node Type\": \"Sort\"", plan);
    }

    [Fact]
    public async Task A_page_is_an_ordered_range_on_name_and_id()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await SeedAsync(owner);
        var plan = await ExplainAsAppUserAsync(
            """
            SELECT s.id FROM tenancy.sites s
            WHERE s.org_id = @org AND s.name >= @n AND (s.name > @n OR s.id > @id)
            ORDER BY s.name, s.id LIMIT 50
            """,
            Org(fixture),
            new NpgsqlParameter("n", "Indexed"),
            new NpgsqlParameter("id", Guid.Empty)
        );
        AssertUsesIndex(plan, "IX_sites_org_id_name_id", "name");
        Assert.DoesNotContain("\"Node Type\": \"Sort\"", plan);
    }
}
