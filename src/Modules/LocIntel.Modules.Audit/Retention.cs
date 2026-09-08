using LocIntel.Contracts;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Audit;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace LocIntel.Modules.Audit;

/// <summary>Per-org purge: audit.retention_days (tiered entitlement) finally gets its consumer.</summary>
public sealed record PurgeAuditData;

public static class PurgeAuditDataHandler
{
    [Wolverine.Attributes.Transactional(typeof(AuditDbContext))]
    public static async Task Handle(
        PurgeAuditData _,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        IAuditPolicyProvider policies,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"PurgeAuditData arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var policy = await policies.GetAsync(org, ct);
        var cutoff = time.GetUtcNow().AddDays(-policy.RetentionDays);
        await db.Changes.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        await db.DomainEvents.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        await db.AuthzDecisions.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        await db.Accesses.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        await db.WebhookDeliveries.Where(d => d.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
    }
}

/// <summary>Daily enumerator (ADR 24 fan-out, same shape as the horizon roll and meter compaction).</summary>
public sealed class AuditRetentionService(IServiceProvider services)
    : PerOrgSweepService<PurgeAuditData>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
}
