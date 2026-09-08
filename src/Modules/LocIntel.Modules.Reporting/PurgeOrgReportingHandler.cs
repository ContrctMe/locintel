using LocIntel.Contracts;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Reporting;

public static class PurgeOrgReportingHandler
{
    [Transactional(typeof(ReportingDbContext))]
    public static async Task Handle(
        PurgeOrgReporting _,
        ReportingDbContext db,
        ITenantContext tenant,
        Wolverine.IMessageBus bus,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("Reporting purge requires an organization.");
        var now = DateTimeOffset.UtcNow;
        await db
            .Jobs.Where(x => x.OrgId == org)
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(x => x.State, ReportJobState.Purging)
                        .SetProperty(x => x.Revision, x => x.Revision + 1)
                        .SetProperty(x => x.ExpiresAt, now)
                        .SetProperty(x => x.MetadataExpiresAt, now),
                ct
            );
        await db.QuotaEntries.Where(x => x.OrgId == org).ExecuteDeleteAsync(ct);
        // Keep artifact intents until the current writer's lease has drained.
        // The durable maintenance tick removes bytes, then metadata, and retries failed deletes.
        await LocIntel.Platform.Messaging.TenantedMessaging.PublishForOrgAsync(
            bus,
            org,
            new MaintainReports()
        );
    }
}
