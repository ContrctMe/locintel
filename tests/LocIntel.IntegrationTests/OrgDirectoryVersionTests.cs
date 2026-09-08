using LocIntel.Api;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Identity.Users;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The third projection rule (docs/cross-tenant-sharing.md), through the
/// real org_directory handler: an older event delivered after a newer one,
/// or the newer one delivered twice, never overwrites the row. The events
/// are INVOKED rather than published so the arrival order is the test's,
/// not the queue's, and each handling is awaited - a published stale event
/// that changes nothing is indistinguishable from one not yet handled.
/// Invoke runs the same handler chain, transaction and lock included.
/// </summary>
public class OrgDirectoryVersionTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private IdentityDbContext Identity() =>
        (IdentityDbContext)
            ApiFixture.CreateCatalogContext(
                ModuleCatalog.All.Single(m => m.Name == "identity"),
                fixture.PostgresConnectionString
            );

    private static OrganizationUpserted Event(OrgId org, string name, long version) =>
        new(org, name, $"v-{org.Value:N}", RegionId.Default, null, version);

    [Fact]
    public async Task An_older_event_arriving_after_a_newer_one_is_ignored()
    {
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(Event(org, "Newer", 7));
        await bus.InvokeAsync(Event(org, "Older", 3)); // late on a busy outbox

        var row = await RowAsync(org);
        Assert.Equal("Newer", row.Name);
        Assert.Equal(7, row.SourceVersion);
    }

    [Fact]
    public async Task A_redelivered_event_changes_nothing()
    {
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(Event(org, "Once", 4));
        var first = await RowAsync(org);
        await bus.InvokeAsync(Event(org, "Twice", 4)); // same version, different payload

        var again = await RowAsync(org);
        Assert.Equal("Once", again.Name);
        Assert.Equal(first.SyncedAt, again.SyncedAt); // not even touched
    }

    [Fact]
    public async Task A_newer_event_still_applies()
    {
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(Event(org, "First", 1));
        await bus.InvokeAsync(Event(org, "Second", 2));

        Assert.Equal("Second", (await RowAsync(org)).Name);
    }

    [Theory]
    [InlineData(2_147_483_647)]
    [InlineData(2_147_483_648)]
    [InlineData(uint.MaxValue)]
    public async Task A_migrated_row_accepts_its_first_real_version(long version)
    {
        var org = OrgId.New();
        await using (var db = Identity())
        {
            db.OrgDirectory.Add(
                new OrgDirectoryEntry
                {
                    OrgId = org,
                    Name = "Before synchronization",
                    Slug = $"v-{org.Value:N}",
                    Region = RegionId.Default,
                    SourceVersion = 0,
                }
            );
            await db.SaveChangesAsync();
        }

        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(Event(org, "Synchronized", version));

        var row = await RowAsync(org);
        Assert.Equal("Synchronized", row.Name);
        Assert.Equal(version, row.SourceVersion);
    }

    [Fact]
    public async Task An_incoming_zero_does_not_create_a_projection()
    {
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(Event(org, "Invalid", 0));

        await using var db = Identity();
        Assert.False(await db.OrgDirectory.AnyAsync(d => d.OrgId == org));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2_147_483_648)]
    [InlineData(uint.MaxValue)]
    public async Task An_incoming_zero_never_changes_an_existing_projection(long applied)
    {
        var org = OrgId.New();
        await using (var db = Identity())
        {
            db.OrgDirectory.Add(
                new OrgDirectoryEntry
                {
                    OrgId = org,
                    Name = "Original",
                    Slug = $"v-{org.Value:N}",
                    Region = RegionId.Default,
                    SourceVersion = applied,
                }
            );
            await db.SaveChangesAsync();
        }
        var before = await RowAsync(org);
        using var scope = fixture.Factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(Event(org, "Invalid", 0));

        var after = await RowAsync(org);
        Assert.Equal("Original", after.Name);
        Assert.Equal(applied, after.SourceVersion);
        Assert.Equal(before.SyncedAt, after.SyncedAt);
    }

    [Fact]
    public async Task Wrapped_versions_apply_and_pre_wrap_redeliveries_are_ignored()
    {
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(Event(org, "Before wrap", uint.MaxValue));
        await bus.InvokeAsync(Event(org, "After wrap", 3));
        await bus.InvokeAsync(Event(org, "Stale", uint.MaxValue));
        await bus.InvokeAsync(Event(org, "Duplicate", 3));

        var row = await RowAsync(org);
        Assert.Equal("After wrap", row.Name);
        Assert.Equal(3, row.SourceVersion);
    }

    [Fact]
    public async Task Concurrent_high_versions_and_zero_leave_the_newest_valid_projection()
    {
        var org = OrgId.New();
        await Task.WhenAll(
            new long[] { 2_147_483_648, 2_147_483_649, 0, 2_147_483_649 }.Select(async version =>
            {
                using var scope = fixture.Factory.Services.CreateScope();
                await scope
                    .ServiceProvider.GetRequiredService<IMessageBus>()
                    .InvokeAsync(Event(org, $"Version {version}", version));
            })
        );

        var row = await RowAsync(org);
        Assert.Equal(2_147_483_649, row.SourceVersion);
        Assert.Equal("Version 2147483649", row.Name);
    }

    private async Task<OrgDirectoryEntry> RowAsync(OrgId org)
    {
        await using var db = Identity();
        return await db.OrgDirectory.SingleAsync(d => d.OrgId == org);
    }
}
