using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Spatial;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// The <c>sites</c> layer (ADR 50 §3-4): every site in scope with its
/// lifecycle status. The map's default layer, and the reference point layer
/// - the facts are the site rows themselves.
/// </summary>
public sealed class SitesDataLayer(TenancyDbContext db) : IDataLayer
{
    public string Name => "sites";
    public string Title => "Sites";
    public string Description => "Every site in scope, by lifecycle status.";
    public string Capability => Capabilities.SitesRead;
    public int MinPointZoom => 9;

    public IReadOnlyList<DataLayerStatus> Statuses { get; } =
    [
        new(nameof(SiteStatus.Open), "Open", "#22c55e"),
        new(nameof(SiteStatus.ComingSoon), "Coming soon", "#60a5fa"),
        new(nameof(SiteStatus.TemporarilyClosed), "Temporarily closed", "#f59e0b"),
        new(nameof(SiteStatus.Closed), "Closed", "#71717a"),
    ];

    public Task<byte[]> RenderAsync(DataLayerTileRequest request, CancellationToken ct = default) =>
        DataLayerTiles.RenderAsync(
            db.Database,
            Name,
            MinPointZoom,
            DataLayerTiles.SitesSql,
            request,
            ct: ct
        );
}
