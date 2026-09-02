using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LocIntel.Contracts;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Kernel;
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
                || c.LastSyncedAt.Value.AddHours(c.SyncIntervalHours!.Value) <= now
            )
            .Select(c => c.Id);
        foreach (var connectorId in due)
            await bus.PublishAsync(
                new SyncSiteConnector(connectorId),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
    }
}

/// <summary>
/// Hourly enumerator fanning out per-org due-connector sweeps (ADR 24
/// pattern, like meter compaction and horizon rolls). A sync that lands stays
/// a STAGED batch - scheduled pulls never auto-commit; a human still reviews
/// the diff.
/// </summary>
public sealed class ConnectorScheduleService(IServiceProvider services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await using var scope = services.CreateAsyncScope();
                var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationLookup>();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                foreach (var orgId in await orgs.ListIdsAsync(stoppingToken))
                    await bus.PublishAsync(
                        new SyncDueConnectors(),
                        new DeliveryOptions { TenantId = orgId.Value.ToString() }
                    );
            }
        }
        catch (OperationCanceledException) { } // shutdown
    }
}
