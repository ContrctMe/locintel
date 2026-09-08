using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.Spatial;

/// <summary>
/// A data layer (ADR 50 §3): a registered query over site facts, computed at
/// tile time and never materialized, so it cannot go stale and carries no
/// ownership question. Modules declare one by implementing this and
/// registering it as <c>IDataLayer</c>; the composition root serves every
/// registered layer at <c>/api/tiles/{layer}/{z}/{x}/{y}</c> behind the
/// layer's own capability, with scope applied inside the SQL.
/// </summary>
public interface IDataLayer
{
    /// <summary>The route segment and the MVT layer name: a slug.</summary>
    string Name { get; }

    string Title { get; }

    string Description { get; }

    /// <summary>The capability gate 2 asks for; gate 3's scope then filters the facts.</summary>
    string Capability { get; }

    /// <summary>Below this zoom the tile carries clusters; from here up, individual points.</summary>
    int MinPointZoom { get; }

    /// <summary>What the <c>status</c> property on each point can be, in legend order.</summary>
    IReadOnlyList<DataLayerStatus> Statuses { get; }

    Task<byte[]> RenderAsync(DataLayerTileRequest request, CancellationToken ct = default);
}

/// <summary>One value of a layer's <c>status</c> property, with the colour the map paints it.</summary>
public sealed record DataLayerStatus(string Key, string Label, string Color);

/// <summary>
/// One tile of one layer for one principal: the org (never ambient), the
/// grant's subtree paths when scope is limited, and the console's chosen node.
/// </summary>
public sealed record DataLayerTileRequest(
    int Z,
    int X,
    int Y,
    OrgId Org,
    IReadOnlyList<string>? ScopePaths,
    string? UnderPath
);
