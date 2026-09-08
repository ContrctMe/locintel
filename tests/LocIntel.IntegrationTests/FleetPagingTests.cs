using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

public class FleetPagingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task All_201_sites_are_reachable_without_cross_tenant_rows()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await owner.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Fleet", levels = new[] { "Region" } }
        );
        created.EnsureSuccessStatusCode();
        var rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
        var expected = new HashSet<Guid>();
        // Cardinality setup bypasses provisioning quotas; HTTP reads use app_user/RLS.
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
            var node = await db
                .HierarchyNodes.IgnoreQueryFilters()
                .SingleAsync(n => n.Id == rootId);
            for (var i = 0; i < 201; i++)
            {
                var id = SiteId.New();
                expected.Add(id.Value);
                db.Sites.Add(
                    new Site
                    {
                        Id = id,
                        OrgId = fixture.OrgA,
                        NodeId = rootId,
                        Path = node.Path,
                        Name = $"Fleet {i:D3}",
                        TimeZone = "Etc/UTC",
                    }
                );
            }
            await db.SaveChangesAsync();
        }
        var actual = new HashSet<Guid>();
        string? after = "";
        var pages = 0;
        while (after is { } cursor)
        {
            Assert.True(++pages <= 5, "Pagination must terminate after five pages");
            var page = await owner.GetFromJsonAsync<JsonElement>(
                cursor.Length == 0 ? "/api/sites?limit=50" : $"/api/sites?limit=50&after={cursor}"
            );
            Assert.Equal(201, page.GetProperty("total").GetInt32());
            foreach (var site in page.GetProperty("items").EnumerateArray())
                Assert.True(
                    actual.Add(site.GetProperty("id").GetGuid()),
                    "Duplicate site across pages"
                );
            var next = page.GetProperty("next");
            after = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
        }
        Assert.True(expected.SetEquals(actual));
        var other = await fixture.LoginAsync(ApiFixture.UserB);
        var hidden = await other.GetFromJsonAsync<JsonElement>("/api/sites?limit=200");
        Assert.Equal(0, hidden.GetProperty("total").GetInt32());
        Assert.Empty(hidden.GetProperty("items").EnumerateArray());
    }
}
