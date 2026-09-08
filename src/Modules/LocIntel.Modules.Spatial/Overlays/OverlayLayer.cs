using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Spatial.Overlays;

/// <summary>
/// An org-drawn overlay (ADR 50 §3): territories, delivery zones, trade
/// areas, regions that mirror the hierarchy. One owning org (ADR 48), a
/// kind and a style the map reads, and an anchor node whose materialized
/// path is the layer's scope: the grant that covers the node sees the layer,
/// exactly as it sees the sites under it. An unanchored layer is anchored to
/// the org's root at creation, so <see cref="Path"/> is never empty.
/// Deletion tier 2 (ADR 25): user content, soft-delete with restore; the
/// features go with the layer and come back with it.
/// </summary>
public sealed class OverlayLayer : IPathScoped, ISoftDeletable
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Name { get; set; }

    /// <summary>What the shapes mean to the org: territory, zone, region... free text, short.</summary>
    public required string Kind { get; set; }

    /// <summary>Style JSON the map applies (fill, stroke, opacity); null = the map's default for the kind.</summary>
    public string? Style { get; set; }

    /// <summary>The hierarchy node the layer is anchored to (the org root when none was given).</summary>
    public required Guid NodeId { get; set; }

    /// <summary>Keyed by hierarchy id, as ADR 2/4 require of any stamped path.</summary>
    public required Guid HierarchyId { get; set; }

    /// <summary>The anchor's materialized path at write time; scope filters on it.</summary>
    public required LTree Path { get; set; }

    /// <summary>Bumped whenever the feature set is replaced: the tile ETag's cheap input.</summary>
    public int Version { get; set; }

    public required Guid CreatedBy { get; init; }

    /// <summary>UTC instant (ADR 26).</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC instant (ADR 26): last rename, restyle or feature replacement.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC instant (ADR 26); set = in the trash, restorable.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
