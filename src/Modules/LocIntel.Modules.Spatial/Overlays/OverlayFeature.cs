using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocIntel.Modules.Spatial.Overlays;

/// <summary>
/// One shape in an overlay layer (ADR 50 §3): a geography in SRID 4326 with
/// the properties the org attached to it. The ancestor path is stamped at
/// write time from the layer's anchor (ADR 2/4), so a tile query filters
/// features by scope with the same predicate that filters sites.
/// Deletion tier 3 (ADR 25): rows are replaced wholesale with the upload
/// that carries them; the layer's soft delete is what a user restores.
/// </summary>
public sealed class OverlayFeature : IPathScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid LayerId { get; init; }

    /// <summary>Stored as geography(Geometry, 4326) (ADR 50): meters and planet-correct boxes by default.</summary>
    public required Geometry Geom { get; set; }

    /// <summary>The GeoJSON feature's properties, verbatim (jsonb); the tile expands them.</summary>
    public string? Properties { get; set; }

    public required Guid HierarchyId { get; init; }
    public required LTree Path { get; init; }

    /// <summary>UTC instant (ADR 26).</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
