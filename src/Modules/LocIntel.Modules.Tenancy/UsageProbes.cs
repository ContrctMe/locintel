using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Tenancy;

/// <summary>Tenancy owns sites: it reports the count for sites.max preflight (ADR 11).</summary>
public sealed class MaxSitesProbe(TenancyDbContext db) : IEntitlementUsageProbe
{
    public string Code => EntitlementCatalog.MaxSites;

    public async ValueTask<long> CurrentUsageAsync(OrgId org, CancellationToken ct = default) =>
        await db.Sites.IgnoreQueryFilters().Where(s => s.OrgId == org).LongCountAsync(ct);
}

/// <summary>Levels currently defined below the root of the authoritative tree.</summary>
public sealed class HierarchyDepthProbe(TenancyDbContext db) : IEntitlementUsageProbe
{
    public string Code => EntitlementCatalog.HierarchyDepth;

    public async ValueTask<long> CurrentUsageAsync(OrgId org, CancellationToken ct = default) =>
        await db
            .Hierarchies.IgnoreQueryFilters()
            .Where(h => h.OrgId == org && h.IsAuthoritative)
            .Select(h => (long)h.Levels.Length)
            .FirstOrDefaultAsync(ct);
}
