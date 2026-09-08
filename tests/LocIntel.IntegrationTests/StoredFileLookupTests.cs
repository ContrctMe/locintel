using LocIntel.Modules.Storage;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

public class StoredFileLookupTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Bulk_lookup_preserves_tenant_and_origin_rules_across_chunks()
    {
        var tenant = new TenantContext();
        tenant.Set(fixture.OrgA, RegionId.Default);
        var own = Enumerable.Range(0, 1001).Select(_ => NewFile(fixture.OrgA)).ToArray();
        var foreign = NewFile(fixture.OrgB);
        var derived = NewFile(fixture.OrgA, "report");
        await using (
            var seed = new StorageDbContext(
                new DbContextOptionsBuilder<StorageDbContext>()
                    .UseNpgsql(fixture.PostgresConnectionString)
                    .Options,
                tenant
            )
        )
        {
            seed.Files.AddRange(own.Append(foreign).Append(derived));
            await seed.SaveChangesAsync();
        }
        await using var db = new StorageDbContext(
            new DbContextOptionsBuilder<StorageDbContext>()
                .UseNpgsql(fixture.AppConnectionString)
                .AddInterceptors(TenantSessionInterceptor.Instance)
                .Options,
            tenant
        );
        var lookup = new StoredFileLookup(db);
        Assert.Empty(await lookup.GetManyAsync([]));
        var found = await lookup.GetManyAsync(
            own.Select(f => f.Id)
                .Concat([own[0].Id, foreign.Id, derived.Id, Guid.NewGuid()])
                .ToArray()
        );
        Assert.Equal(own.Length, found.Count);
        Assert.Equal(await lookup.GetAsync(own[0].Id), found[own[0].Id]);
        Assert.DoesNotContain(foreign.Id, found.Keys);
        Assert.DoesNotContain(derived.Id, found.Keys);
        Assert.DoesNotContain(
            db.ChangeTracker.Entries<FileObject>(),
            e => e.Entity.Id != own[0].Id
        );
    }

    private static FileObject NewFile(OrgId org, string? origin = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Origin = origin,
            Key = Guid.NewGuid().ToString(),
            Name = "lookup.txt",
            ContentType = "text/plain",
            MaxBytes = 1,
            CreatedBy = Guid.NewGuid(),
        };
}
