using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Spatial;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// The <c>open-now</c> layer (ADR 50 §3): which sites are open at this
/// instant, read from the materialized occurrence projection (ADR 27/28) -
/// the same rows the open-now list query uses, so the map and the list can
/// never disagree. A site with schedules but no window right now is closed;
/// one with no schedules at all is unscheduled, which is a data gap worth
/// seeing, not a closure.
/// </summary>
public sealed class OpenNowDataLayer(TenancyDbContext db) : IDataLayer
{
    public string Name => "open-now";
    public string Title => "Open now";
    public string Description => "Which sites are open at this moment, from their schedules.";
    public string Capability => Capabilities.SitesRead;
    public int MinPointZoom => 9;

    public IReadOnlyList<DataLayerStatus> Statuses { get; } =
    [
        new("open", "Open now", "#22c55e"),
        new("closed", "Closed now", "#71717a"),
        new("unscheduled", "No schedule", "#f59e0b"),
    ];

    private const string Facts = """
        SELECT s.id, s.name,
               CASE
                   WHEN EXISTS (
                       SELECT 1 FROM tenancy.site_open_windows w
                       WHERE w.site_id = s.id AND w.starts_at_utc <= now() AND w.ends_at_utc > now()
                   ) THEN 'open'
                   WHEN EXISTS (SELECT 1 FROM tenancy.site_schedules sc WHERE sc.site_id = s.id) THEN 'closed'
                   ELSE 'unscheduled'
               END AS status,
               s.path_text, s.cell, s.location
        FROM tenancy.sites s
        WHERE s.org_id = @org AND s.cell IS NOT NULL
        """;

    public Task<byte[]> RenderAsync(DataLayerTileRequest request, CancellationToken ct = default) =>
        DataLayerTiles.RenderAsync(db.Database, Name, MinPointZoom, Facts, request, ct: ct);
}
