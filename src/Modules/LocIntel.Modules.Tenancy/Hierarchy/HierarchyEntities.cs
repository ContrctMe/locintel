using Microsoft.EntityFrameworkCore;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Hierarchy;

/// <summary>
/// An org's rollup structure (ADR 4: hierarchy_id from day one, exactly one
/// authoritative tree provisioned in v1). Levels are per-org names, ordered
/// root-first ("Division", "Region", "Market"); the count cap is an
/// entitlement (step 4). Current-only (ADR 2): re-parenting rewrites paths,
/// fact tables stamp paths at write time to keep history honest.
/// </summary>
public sealed class OrgHierarchy : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Name { get; set; }
    public required string[] Levels { get; set; }
    public bool IsAuthoritative { get; init; } = true;

    public static OrgHierarchy Create(OrgId orgId, string name, string[] levels) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = orgId,
            Name = name,
            Levels = levels,
        };
}

/// <summary>
/// A node in the tree (ADR 3: separate from sites - a Division carries none of
/// a site's attributes). Path is a materialized ltree: root "n{hex}", children
/// append. Depth 0 is the root; Depth indexes into OrgHierarchy.Levels.
/// Deletion tier 1: nodes are restructured or absorbed, never soft-deleted.
/// </summary>
public sealed class HierarchyNode : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid HierarchyId { get; init; }
    public Guid? ParentId { get; set; }
    public required string Name { get; set; }
    public required int Depth { get; set; }
    public required LTree Path { get; set; }

    public static string Label(Guid id) => "n" + id.ToString("N");

    public static HierarchyNode CreateRoot(OrgId orgId, Guid hierarchyId, string name)
    {
        var id = Guid.CreateVersion7();
        return new HierarchyNode
        {
            Id = id,
            OrgId = orgId,
            HierarchyId = hierarchyId,
            ParentId = null,
            Name = name,
            Depth = 0,
            Path = new LTree(Label(id)),
        };
    }

    public static HierarchyNode CreateChild(HierarchyNode parent, string name)
    {
        var id = Guid.CreateVersion7();
        return new HierarchyNode
        {
            Id = id,
            OrgId = parent.OrgId,
            HierarchyId = parent.HierarchyId,
            ParentId = parent.Id,
            Name = name,
            Depth = parent.Depth + 1,
            Path = new LTree($"{parent.Path}.{Label(id)}"),
        };
    }
}
