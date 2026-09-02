using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Audit;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Audit;

/// <summary>
/// Resolves the effective policy (ADR 12): org config INTERSECTED with the
/// entitlement ceiling - a tenant can switch read-logging on only if the plan
/// includes it; retention comes straight from the tiered entitlement.
/// </summary>
public sealed class AuditPolicyService(AuditDbContext db, IEntitlements entitlements)
    : IAuditPolicyProvider
{
    public async ValueTask<AuditPolicy> GetAsync(OrgId org, CancellationToken ct = default)
    {
        var config = await db
            .Configs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.OrgId == org, ct);
        var readsEntitled = await entitlements.HasAsync(
            org,
            EntitlementCatalog.AuditReadLogging,
            ct
        );
        var retention = (int)
            await entitlements.LimitAsync(org, EntitlementCatalog.AuditRetentionDays, ct);
        return new AuditPolicy(
            LogGrants: config?.LogGrants ?? AuditPolicy.Floor.LogGrants,
            LogReads: (config?.LogReads ?? AuditPolicy.Floor.LogReads) && readsEntitled,
            RetentionDays: retention
        );
    }
}
