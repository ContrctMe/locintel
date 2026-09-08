using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Tenancy.Hierarchy;

/// <summary>
/// The <see cref="IHierarchyDirectory"/> contract over Tenancy's nodes: the
/// tenant filter on the context decides whose tree is asked (read-time,
/// never ambient), so an id from another org is simply not found.
/// </summary>
public sealed class HierarchyDirectory(TenancyDbContext db) : IHierarchyDirectory
{
    public Task<NodeInfo?> FindNodeAsync(Guid nodeId, CancellationToken ct = default) =>
        db
            .HierarchyNodes.Where(n => n.Id == nodeId)
            .Select(n => new NodeInfo(n.Id, n.HierarchyId, n.Path.ToString(), n.Name, n.Depth))
            .FirstOrDefaultAsync(ct);

    public Task<NodeInfo?> FindRootAsync(CancellationToken ct = default) =>
        db
            .Hierarchies.Where(h => h.IsAuthoritative)
            .Join(
                db.HierarchyNodes.Where(n => n.ParentId == null),
                h => h.Id,
                n => n.HierarchyId,
                (h, n) => new NodeInfo(n.Id, n.HierarchyId, n.Path.ToString(), n.Name, n.Depth)
            )
            .FirstOrDefaultAsync(ct);
}
