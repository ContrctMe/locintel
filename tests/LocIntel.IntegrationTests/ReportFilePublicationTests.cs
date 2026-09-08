using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

public sealed class ReportFilePublicationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Organization_purge_fences_delayed_file_publication()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgB, RegionId.Default);
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var id = Guid.CreateVersion7();
        var key = $"{RegionId.Default.Value}/{fixture.OrgB.Value}/reports/{id}";
        using var bytes = new MemoryStream("%PDF-test"u8.ToArray());
        await store.WriteAsync(key, bytes, "application/pdf");
        var message = new PublishGeneratedFile(
            id,
            key,
            "report.pdf",
            "application/pdf",
            bytes.Length,
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow,
            [Guid.CreateVersion7()],
            "report",
            Guid.CreateVersion7()
        );
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var options = new DeliveryOptions { TenantId = fixture.OrgB.Value.ToString() };
        await bus.InvokeAsync(message, options);
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        Assert.True(await db.Files.AnyAsync(x => x.Id == id));
        await bus.InvokeAsync(new PurgeOrgFiles(), options);
        await bus.InvokeAsync(message, options);
        Assert.False(await db.Files.AnyAsync(x => x.Id == id));
        Assert.True(await db.PurgedOrganizations.AnyAsync(x => x.OrgId == fixture.OrgB));
        Assert.Null(await store.GetLengthAsync(key));
    }
}
