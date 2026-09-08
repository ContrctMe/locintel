using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Reporting;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class ReportingBoundsTests(ReportingBoundsFixture fixture)
    : IClassFixture<ReportingBoundsFixture>
{
    [Theory]
    [InlineData("99", "\"selected\"")]
    [InlineData("\"99\"", "\"selected\"")]
    [InlineData("\"bulk\"", "99")]
    [InlineData("\"bulk\"", "\"99\"")]
    public async Task Undefined_enums_are_rejected_before_creating_jobs_or_reserving_quota(
        string mode,
        string selection
    )
    {
        var (client, sites) = await Setup(1);
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var jobsBefore = await db.Jobs.CountAsync();
        var quotaBefore = await db.QuotaEntries.CountAsync();
        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                reportType = "workflow-test",
                mode = JsonSerializer.Deserialize<JsonElement>(mode),
                selection = JsonSerializer.Deserialize<JsonElement>(selection),
                siteIds = sites,
                options = new { },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "Unknown report mode or selection.",
            await response.Content.ReadAsStringAsync()
        );
        Assert.Equal(jobsBefore, await db.Jobs.CountAsync());
        Assert.Equal(quotaBefore, await db.QuotaEntries.CountAsync());
    }

    /// <summary>
    /// A render that never observes cancellation is still bounded. Awaiting one
    /// directly made the item budget advisory: the lease expired while a
    /// synchronous library call ran on, another worker re-claimed the item, and
    /// the same PDF was produced twice with two quota settlements to reconcile.
    /// </summary>
    [Fact]
    public async Task A_render_that_ignores_cancellation_still_fails_once_at_its_deadline()
    {
        var (client, sites) = await Setup(1);
        var id = await Submit(client, sites, new { block = true });
        var job = await Wait(client, id, "Failed");

        var item = Assert.Single(job.GetProperty("items").EnumerateArray());
        Assert.Equal("generation_timeout", item.GetProperty("errorCode").GetString());
        Assert.Equal(1, item.GetProperty("attempt").GetInt32());
        Assert.Empty(job.GetProperty("artifacts").EnumerateArray());

        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        // One attempt, one artifact intent, and nothing published: the abandoned
        // render's late bytes reach a stream that refuses them.
        var artifact = Assert.Single(await db.Artifacts.Where(x => x.JobId == id).ToListAsync());
        Assert.False(artifact.Ready);
        Assert.False(artifact.FilePublished);
        // The failed item released its reservation rather than holding capacity.
        Assert.Empty(await db.QuotaEntries.Where(x => x.JobId == id).ToListAsync());
    }

    /// <summary>
    /// Reading a page of runs resolves its sites once. It used to resolve the
    /// requester, two scopes and a site query per run, so a full page of fifty
    /// cost hundreds of queries - on a list the console polls while work runs.
    /// </summary>
    [Fact]
    public async Task Listing_runs_resolves_sites_once_for_the_whole_page()
    {
        var (client, sites) = await Setup(1);
        var siteId = sites[0];
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(fixture.OrgA, RegionId.Default);
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var user = await scope
                .ServiceProvider.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>()
                .Users.SingleAsync(x => x.Email == ApiFixture.UserA);
            for (var n = 0; n < 50; n++)
            {
                var job = new ReportJob
                {
                    OrgId = fixture.OrgA,
                    RequestedBy = user.Id,
                    ReportType = "workflow-test",
                    DefinitionVersion = 1,
                    Mode = ReportMode.Single,
                    Selection = ReportSelection.Selected,
                    OptionsJson = "{}",
                    SiteIds = [siteId],
                    State = ReportJobState.Completed,
                };
                job.Items.Add(
                    new ReportItem
                    {
                        OrgId = fixture.OrgA,
                        JobId = job.Id,
                        SiteIds = [siteId],
                        State = ReportItemState.Succeeded,
                    }
                );
                db.Jobs.Add(job);
            }
            await db.SaveChangesAsync();
        }

        var calls = fixture.Factory.Services.GetRequiredService<SiteSourceCalls>();
        calls.Reset();
        var page = await client.GetFromJsonAsync<JsonElement>("/api/reports");
        Assert.Equal(50, page.EnumerateArray().Count());
        Assert.Equal(1, calls.Selects);
        // And the page carries names, so nothing downstream has to resolve them again.
        Assert.All(
            page.EnumerateArray(),
            run =>
                Assert.Equal(
                    "Bounds site",
                    Assert
                        .Single(run.GetProperty("sites").EnumerateArray())
                        .GetProperty("name")
                        .GetString()
                )
        );
    }

    private async Task<(HttpClient Client, Guid[] Sites)> Setup(int count)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var ids = Enumerable.Range(0, count).Select(_ => SiteId.New()).ToArray();
        foreach (var id in ids)
            db.Sites.Add(
                new Site
                {
                    Id = id,
                    OrgId = fixture.OrgA,
                    NodeId = Guid.NewGuid(),
                    Name = "Bounds site",
                    TimeZone = "UTC",
                    Path = new LTree("bounds." + Site.Label(id)),
                }
            );
        await db.SaveChangesAsync();
        return (await fixture.LoginAsync(ApiFixture.UserA), ids.Select(x => x.Value).ToArray());
    }

    private static async Task<Guid> Submit(HttpClient client, Guid[] ids, object options)
    {
        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                reportType = "workflow-test",
                mode = "single",
                selection = "selected",
                siteIds = ids,
                options,
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }

    private static async Task<JsonElement> Wait(HttpClient client, Guid id, string state)
    {
        JsonElement job = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                job = await client.GetFromJsonAsync<JsonElement>($"/api/reports/{id}");
                return job.GetProperty("state").GetString() == state;
            },
            $"report {id} to reach {state}"
        );
        return job;
    }
}
