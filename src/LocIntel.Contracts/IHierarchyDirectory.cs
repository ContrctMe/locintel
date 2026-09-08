namespace LocIntel.Contracts;

/// <summary>
/// What another module may know about a hierarchy node (ADR 37 direction:
/// Tenancy implements, modules above consume). It carries the hierarchy id
/// and the materialized path because ADR 2/4 require fact tables to stamp
/// the ancestor path KEYED BY hierarchy id at write time - a consumer
/// anchoring its rows to a node needs both, and must not reach into
/// Tenancy's tables to get them.
/// </summary>
public sealed record NodeInfo(Guid Id, Guid HierarchyId, string Path, string Name, int Depth);

/// <summary>
/// Node lookup for modules that anchor org data to the hierarchy without
/// touching Tenancy's tables: a node by id, or the root of the org's
/// authoritative tree (the anchor for anything org-wide).
/// </summary>
public interface IHierarchyDirectory
{
    Task<NodeInfo?> FindNodeAsync(Guid nodeId, CancellationToken ct = default);

    Task<NodeInfo?> FindRootAsync(CancellationToken ct = default);
}
