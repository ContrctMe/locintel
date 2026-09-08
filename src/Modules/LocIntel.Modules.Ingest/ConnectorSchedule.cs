using LocIntel.Contracts;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Ingest;

/// <summary>Per-org sweep: sync every connector whose interval has elapsed (envelope-tenanted).</summary>
public sealed record SyncDueConnectors;

public static class SyncDueConnectorsHandler
{
    [Transactional(typeof(IngestDbContext))]
    public static async Task Handle(
        SyncDueConnectors _,
        Envelope envelope,
        ITenantContext tenant,
        IngestDbContext db,
        TimeProvider time,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"SyncDueConnectors arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var now = time.GetUtcNow();
        // per-org connector counts are tiny: pull the scheduled ones and
        // decide dueness in memory (AddHours over a column does not translate)
        var scheduled = await db
            .Connectors.Where(c => c.SyncIntervalHours != null)
            .Select(c => new
            {
                c.Id,
                c.LastSyncedAt,
                c.SyncIntervalHours,
            })
            .ToListAsync(ct);
        var due = scheduled
            .Where(c =>
                c.LastSyncedAt is null
                || c.SyncIntervalHours is > 0 and <= 8760
                    && c.LastSyncedAt.Value.AddHours(c.SyncIntervalHours.Value) <= now
            )
            .Select(c => c.Id);
        foreach (var connectorId in due)
            // A pending connector must not block other due work; admission still caps the queue.
            await ConnectorQueue.EnqueueAsync(db, org, connectorId, bus, ct);
    }
}

/// <summary>
/// Hourly enumerator fanning out per-org due-connector sweeps (ADR 24
/// pattern, like meter compaction and horizon rolls). A sync that lands stays
/// a STAGED batch - scheduled pulls never auto-commit; a human still reviews
/// the diff.
/// </summary>
public sealed class ConnectorScheduleService(IServiceProvider services)
    : PerOrgSweepService<SyncDueConnectors>(services)
{
    // hourly; the handler decides what is due, so a tick at start is harmless
    protected override TimeSpan Interval => TimeSpan.FromHours(1);
}
