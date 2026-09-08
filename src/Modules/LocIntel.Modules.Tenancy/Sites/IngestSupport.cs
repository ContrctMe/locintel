using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Hierarchy;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>Read contract for the ingest diff (ADR 18).</summary>
public sealed class SiteLookup(TenancyDbContext db) : ISiteLookup
{
    /// <summary>One parameter list per thousand ids: each chunk is a range on the (org_id, external_id) index.</summary>
    private const int Chunk = 1_000;

    public async Task<IReadOnlyList<SiteSnapshot>> ListSitesAsync(
        IReadOnlyCollection<string> externalIds,
        CancellationToken ct = default
    )
    {
        var found = new List<SiteSnapshot>(externalIds.Count);
        foreach (var chunk in externalIds.Distinct().Chunk(Chunk))
        {
            var sites = await db
                .Sites.Where(s => s.ExternalId != null && chunk.Contains(s.ExternalId))
                .Select(s => new SiteSnapshot(
                    s.Id,
                    s.ExternalId,
                    s.Name,
                    s.TimeZone,
                    s.Status.ToString()
                ))
                .ToListAsync(ct);
            found.AddRange(sites);
        }
        return found;
    }

    public Task<long> CountSitesAsync(CancellationToken ct = default) =>
        db.Sites.LongCountAsync(ct);

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
        IEntitlements entitlements,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"SiteChangeRequested arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        if (!await CapacityReservations.TryLockAsync(db, org, EntitlementCatalog.MaxSites, ct))
            throw new CapacityBusyException();
        var site = await db.Sites.FirstOrDefaultAsync(s => s.ExternalId == message.ExternalId, ct);
        if (message.CapacityReservationId is { } reservation)
        {
            var consumed = await CapacityReservations.ConsumeAsync(
                db,
                org,
                EntitlementCatalog.MaxSites,
                reservation,
                ct
            );
            if (consumed == 0 && site is null)
                throw new InvalidOperationException(
                    "Site create has no capacity reservation; retry or reconcile its durable intent."
                );
        }
        else if (message.Action == "create" && site is null)
        {
            // Compatibility for pre-upgrade messages. New imports always reserve
            // before acceptance; legacy intent cannot bypass the strict ceiling.
            var occupied =
                await db.Sites.LongCountAsync(ct)
                + await CapacityReservations.PendingAsync(db, org, EntitlementCatalog.MaxSites, ct);
            var decision = await entitlements.CheckLimitAsync(
                org,
                EntitlementCatalog.MaxSites,
                occupied,
                1,
                ct
            );
            if (!decision.IsAllowed)
                throw new InvalidOperationException(
                    "Legacy site import exceeds capacity; reconcile it before retrying."
                );
        }
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
                await bus.AuditAsync(
                    org,
                    AuditActor.System,
                    "site.closed",
                    new
                    {
                        siteId = site.Id.Value,
                        site.Name,
                        source = "ingest",
                    }
                );
                break;
            }
            // idempotent re-delivery lands here: nothing to do
        }
    }
}
