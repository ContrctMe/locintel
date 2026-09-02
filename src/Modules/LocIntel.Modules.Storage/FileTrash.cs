using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Wolverine;

namespace LocIntel.Modules.Storage;

/// <summary>Per-org trash sweep: files past the restore window get their bytes erased.</summary>
public sealed record PurgeFileTrash;

public static class PurgeFileTrashHandler
{
    [Wolverine.Attributes.Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        PurgeFileTrash _,
        Envelope envelope,
        ITenantContext tenant,
        StorageDbContext db,
        IObjectStore store,
        IConfiguration configuration,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"PurgeFileTrash arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var window = configuration.GetValue<int?>("Storage:TrashRetentionDays") ?? 30;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-window);
        var expired = await db
            .Files.Where(f => f.Status == FileStatus.Deleted && f.DeletedAt < cutoff)
            .ToListAsync(ct);
        foreach (var file in expired)
        {
            await store.DeleteAsync(file.Key, ct);
            if (file.PreviewKey is { } previewKey)
                await store.DeleteAsync(previewKey, ct);
            file.Status = FileStatus.Erased;
            file.PreviewKey = null;
            await bus.PublishAsync(
                new RecordDomainAudit(
                    "file.erased",
                    System.Text.Json.JsonSerializer.Serialize(
                        new
                        {
                            file.Id,
                            file.Name,
                            source = "trash-window",
                        }
                    )
                ),
                new DeliveryOptions
                {
                    TenantId = org.Value.ToString(),
                    Headers = { ["locintel-actor-tier"] = "system" },
                }
            );
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Daily enumerator (same shape as the audit retention sweep).</summary>
public sealed class FileTrashService(IServiceProvider services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            do
            {
                await using var scope = services.CreateAsyncScope();
                var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationLookup>();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                foreach (var orgId in await orgs.ListIdsAsync(stoppingToken))
                    await bus.PublishAsync(
                        new PurgeFileTrash(),
                        new DeliveryOptions { TenantId = orgId.Value.ToString() }
                    );
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { } // shutdown
    }
}
