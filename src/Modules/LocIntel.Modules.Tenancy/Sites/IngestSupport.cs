using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Hierarchy;
using LocIntel.Platform.Kernel;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>Read contract for the ingest diff (ADR 18).</summary>
public sealed class SiteLookup(TenancyDbContext db) : ISiteLookup
{
    public async Task<IReadOnlyList<SiteSnapshot>> ListSitesAsync(CancellationToken ct = default) =>
        (await db.Sites.ToListAsync(ct))
            .Select(s => new SiteSnapshot(
                s.Id,
                s.ExternalId,
                s.Name,
                s.TimeZone,
                s.Status.ToString()
            ))
            .ToList();

    public async Task<IReadOnlyList<NodeSnapshot>> ListNodesAsync(CancellationToken ct = default)
    {
        var nodes = await db.HierarchyNodes.ToListAsync(ct);
        var byId = nodes.ToDictionary(n => n.Id);
        return nodes.Select(n => new NodeSnapshot(n.Id, NamePath(n, byId))).ToList();
    }

    private static string NamePath(HierarchyNode node, Dictionary<Guid, HierarchyNode> byId)
    {
        // names below the root: "East/Boston" (the root itself is implicit)
        var parts = new List<string>();
        for (var current = node; current.ParentId is { } parentId; current = byId[parentId])
            parts.Insert(0, current.Name);
        return string.Join('/', parts);
    }
}

/// <summary>
/// Applies ingest-requested changes (ADR 17/18): Tenancy owns its writes.
/// Closing is a status transition WITH a domain event - never a delete.
/// Timezone changes trigger the projection rebuild (ADR 28).
/// </summary>
public static class SiteChangeRequestedHandler
{
    [Transactional(typeof(TenancyDbContext))]
    public static async Task Handle(
        SiteChangeRequested message,
        Envelope envelope,
        ITenantContext tenant,
        TenancyDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"SiteChangeRequested arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var site = await db.Sites.FirstOrDefaultAsync(s => s.ExternalId == message.ExternalId, ct);
        switch (message.Action)
        {
            case "create" when site is null && message.NodeId is { } nodeId:
            {
                var node = await db.HierarchyNodes.FirstAsync(n => n.Id == nodeId, ct);
                var id = SiteId.New();
                db.Sites.Add(
                    new Site
                    {
                        Id = id,
                        OrgId = org,
                        NodeId = node.Id,
                        Name = message.Name,
                        TimeZone = message.TimeZone,
                        ExternalId = message.ExternalId,
                        Path = new Microsoft.EntityFrameworkCore.LTree(
                            $"{node.Path}.{Site.Label(id)}"
                        ),
                    }
                );
                await db.SaveChangesAsync(ct);
                break;
            }
            case "update" when site is not null:
            {
                var timeZoneChanged = site.TimeZone != message.TimeZone;
                site.Name = message.Name;
                site.TimeZone = message.TimeZone;
                if (site.Status == SiteStatus.Closed)
                    site.Status = SiteStatus.Open; // source reopened it
                await db.SaveChangesAsync(ct);
                if (timeZoneChanged)
                    await bus.PublishForOrgAsync(org, new RebuildSiteOccurrences(site.Id.Value));
                break;
            }
            case "close" when site is not null && site.Status != SiteStatus.Closed:
            {
                site.Status = SiteStatus.Closed;
                await db.SaveChangesAsync(ct);
                await bus.PublishAsync(
                    new RecordDomainAudit(
                        "site.closed",
                        System.Text.Json.JsonSerializer.Serialize(
                            new
                            {
                                siteId = site.Id.Value,
                                site.Name,
                                source = "ingest",
                            }
                        )
                    ),
                    new DeliveryOptions { TenantId = org.Value.ToString() }
                );
                break;
            }
            // idempotent re-delivery lands here: nothing to do
        }
    }
}
