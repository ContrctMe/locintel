using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        var hasLiveCloudTickets = store.SupportsBoundedUpload && store is not LocalObjectStore;
        var ticketCutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
        var expired = await db
            .Files.Where(f =>
                f.Status == FileStatus.Deleted
                && !f.LegalHold
                && f.DeletedAt < cutoff
                && (!hasLiveCloudTickets || f.CreatedAt < ticketCutoff)
            )
            .ToListAsync(ct);
        foreach (var file in expired)
        {
            await db.TakeAsync(file.Id, ct);
            await db.Entry(file).ReloadAsync(ct);
            if (
                file.Status != FileStatus.Deleted
                || file.LegalHold
                || file.DeletedAt >= cutoff
                || (hasLiveCloudTickets && file.CreatedAt >= ticketCutoff)
            )
                continue;
            await FileBytes.EraseAsync(file, store, ct);
            file.Status = FileStatus.Erased;
            file.PreviewKey = null;
            await bus.AuditAsync(
                org,
                AuditActor.System,
                "file.erased",
                new
                {
                    file.Id,
                    file.Name,
                    source = "trash-window",
                }
            );
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Daily enumerator (same shape as the audit retention sweep).</summary>
public sealed class FileTrashService(IServiceProvider services)
    : PerOrgSweepService<PurgeFileTrash>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
}
