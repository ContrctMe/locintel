using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Kernel;
using Wolverine.Attributes;

namespace LocIntel.Modules.Audit;

/// <summary>
/// The audit module deliberately keeps the org's TRAIL after offboarding -
/// but webhook endpoints and their delivery log are configuration, and
/// configuration goes with the org.
/// </summary>
public static class PurgeOrgWebhooksHandler
{
    [Transactional]
    public static async Task Handle(
        PurgeOrgWebhooks _,
        AuditDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("purge arrived with no tenant on the envelope");
        await db
            .WebhookDeliveries.IgnoreQueryFilters()
            .Where(d => d.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db
            .WebhookEndpoints.IgnoreQueryFilters()
            .Where(e => e.OrgId == org)
            .ExecuteDeleteAsync(ct);
    }
}
