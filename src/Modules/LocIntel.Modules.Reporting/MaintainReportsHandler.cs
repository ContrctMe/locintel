using LocIntel.Contracts;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Reporting;

public static class MaintainReportsHandler
{
    [NonTransactional]
    public static async Task Handle(
        MaintainReports _,
        ITenantContext tenant,
        IServiceScopeFactory scopes,
        IMessageBus bus,
        TimeProvider time,
        ILogger<ReportMaintenanceService> logger,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("Report maintenance requires an organization.");
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(org, tenant.Region);
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var now = time.GetUtcNow();
        var jobs = await db
            .Jobs.AsNoTracking()
            .Where(x =>
                x.OrgId == org
                && (
                    x.State == ReportJobState.Queued
                    || x.State == ReportJobState.Running
                    || x.State == ReportJobState.Purging
                    || x.Artifacts.Any(a => a.Ready && a.ItemId != null && !a.FilePublished)
                    || x.ExpiresAt <= now && x.Artifacts.Any(a => !a.Ready || a.ItemId == null)
                    || x.MetadataExpiresAt <= now
                        && !x.Artifacts.Any(a => a.Ready && a.ItemId != null)
                )
            )
            .OrderBy(x => x.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
        foreach (var job in jobs)
        {
            try
            {
                if (job.LeaseUntil > now)
                {
                    if (job.State == ReportJobState.Purging)
                        await bus.PublishAsync(
                            new MaintainReports(),
                            new DeliveryOptions
                            {
                                TenantId = org.Value.ToString(),
                                ScheduledTime = job.LeaseUntil.Value.AddSeconds(1),
                            }
                        );
                    continue;
                }
                if (job.State != ReportJobState.Purging)
                {
                    // Reconciliation also publishes pre-upgrade PDFs and repairs a missing acknowledgement.
                    var pending = await db
                        .Artifacts.AsNoTracking()
                        .Where(x =>
                            x.OrgId == org
                            && x.JobId == job.Id
                            && x.Ready
                            && x.ItemId != null
                            && !x.FilePublished
                        )
                        .ToListAsync(ct);
                    foreach (var artifact in pending)
                    {
                        var item = await db
                            .Items.AsNoTracking()
                            .SingleAsync(x => x.OrgId == org && x.Id == artifact.ItemId, ct);
                        await bus.PublishForOrgAsync(
                            org,
                            new PublishGeneratedFile(
                                artifact.Id,
                                artifact.Key,
                                artifact.Name,
                                artifact.ContentType,
                                artifact.Bytes,
                                job.RequestedBy,
                                item.GeneratedAt ?? artifact.CreatedAt,
                                item.SiteIds,
                                "report",
                                job.Id
                            )
                        );
                    }
                    if (job.ExpiresAt is null || job.ExpiresAt > now)
                    {
                        // Reached only with no live lease. Running therefore means
                        // the worker is gone, so resume it at once. Queued may
                        // simply be waiting its turn: re-publishing every queued
                        // job on every tick multiplied a backlog's queue traffic,
                        // so a queued job is re-delivered only once Wolverine has
                        // had a full sweep interval to deliver it itself.
                        if (
                            job.State is ReportJobState.Running
                            || (
                                job.State is ReportJobState.Queued
                                && job.CreatedAt < now - ReportMaintenanceService.Sweep
                            )
                        )
                            await bus.PublishForOrgAsync(org, new GenerateReport(job.Id));
                        continue;
                    }
                }
                await using (var tx = await db.Database.BeginTransactionAsync(ct))
                {
                    await db.TakeAsync(job.Id, ct);
                    var current = await db.Jobs.FirstOrDefaultAsync(
                        x => x.Id == job.Id && x.OrgId == org,
                        ct
                    );
                    if (current is null || current.LeaseUntil > time.GetUtcNow())
                        continue;
                    await ReportQuota.ReleaseAsync(db, org, job.Id, ct);
                    if (current.State is ReportJobState.Running or ReportJobState.Queued)
                        current.State = ReportJobState.Expired;
                    current.Revision++;
                    current.LeaseOwner = null;
                    current.LeaseUntil = null;
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                var artifacts = await db
                    .Artifacts.Where(x =>
                        x.OrgId == org
                        && x.JobId == job.Id
                        && (job.State == ReportJobState.Purging || !x.Ready || x.ItemId == null)
                    )
                    .ToListAsync(ct);
                foreach (var artifact in artifacts)
                    await store.DeleteAsync(artifact.Key, ct);
                // Successful PDFs belong to Storage, including its trash and legal-hold lifecycle.
                var ids = artifacts.Select(x => x.Id).ToArray();
                await db
                    .Artifacts.Where(x => x.OrgId == org && ids.Contains(x.Id))
                    .ExecuteDeleteAsync(ct);
                if (
                    job.MetadataExpiresAt <= now
                    && !await db.Artifacts.AnyAsync(
                        x => x.OrgId == org && x.JobId == job.Id && x.Ready && x.ItemId != null,
                        ct
                    )
                )
                    await db
                        .Jobs.Where(x => x.OrgId == org && x.Id == job.Id)
                        .ExecuteDeleteAsync(ct);
                db.ChangeTracker.Clear();
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                // An unreachable object or one poisoned row must not stop the
                // sweep. Jobs are taken in CreatedAt order, so throwing here
                // put the same job first on every tick and nothing behind it
                // in this organization was ever cleaned up.
                logger.LogWarning(
                    "Report maintenance skipped job {JobId} ({ErrorType})",
                    job.Id,
                    e.GetType().Name
                );
                db.ChangeTracker.Clear();
            }
        }
    }
}
