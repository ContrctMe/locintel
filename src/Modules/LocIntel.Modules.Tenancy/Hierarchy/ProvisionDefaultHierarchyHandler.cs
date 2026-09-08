using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Tenancy.Hierarchy;

public static class ProvisionDefaultHierarchyHandler
{
    [Transactional(typeof(TenancyDbContext))]
    public static async Task Handle(
        ProvisionDefaultHierarchy message,
        Envelope envelope,
        ITenantContext tenant,
        TenancyDbContext db,
        IEntitlements entitlements,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"ProvisionDefaultHierarchy arrived with no tenant (TenantId='{envelope.TenantId}')"
            );
        if (org != message.OrgId)
            throw new InvalidOperationException(
                $"ProvisionDefaultHierarchy for {message.OrgId} arrived under tenant {org}"
            );
        // a founder who raced ahead and provisioned by hand wins; the unique
        // index on the authoritative flag makes a tie a retry, then this branch
        if (await db.Hierarchies.AnyAsync(h => h.IsAuthoritative, ct))
            return;

        var organization = await db.Organizations.FirstAsync(o => o.Id == org, ct);
        // the plan caps depth at provisioning (the gate-1 structural example);
        // the defaults fit every catalog plan, but a fork's floor may not
        var depthLimit = await entitlements.LimitAsync(org, EntitlementCatalog.HierarchyDepth, ct);
        var levels = HierarchyDefaults.Levels.Take((int)Math.Max(1, depthLimit)).ToArray();

        var hierarchy = OrgHierarchy.Create(org, organization.Name, levels);
        db.Hierarchies.Add(hierarchy);
        db.HierarchyNodes.Add(HierarchyNode.CreateRoot(org, hierarchy.Id, organization.Name));
        await db.SaveChangesAsync(ct);
    }
}
