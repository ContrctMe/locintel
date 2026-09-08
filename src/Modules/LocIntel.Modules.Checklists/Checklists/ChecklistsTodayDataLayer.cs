using LocIntel.Modules.Checklists.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Spatial;

namespace LocIntel.Modules.Checklists.Checklists;

/// <summary>
/// The <c>checklists-today</c> layer (ADR 50 §3): how far each site is
/// through today's lists, on the SITE's business date (ADR 26 kind 3 - the
/// date is taken in the site's own zone, exactly as the today endpoint does).
/// A module's data layer joins its own facts to the shared site facts; it
/// never touches Tenancy's tables by any other route.
/// </summary>
public sealed class ChecklistsTodayDataLayer(ChecklistsDbContext db) : IDataLayer
{
    public string Name => "checklists-today";
    public string Title => "Checklists today";
    public string Description =>
        "Progress through today's checklists at each site, on its own clock.";
    public string Capability => Capabilities.ChecklistsComplete;
    public int MinPointZoom => 9;

    public IReadOnlyList<DataLayerStatus> Statuses { get; } =
    [
        new("complete", "Complete", "#22c55e"),
        new("partial", "In progress", "#f59e0b"),
        new("pending", "Not started", "#ef4444"),
        new("none", "No lists", "#71717a"),
    ];

    // per-site LATERAL, not an aggregate over every site first: the tile's
    // cell range is applied to `s`, so only the sites in the tile are costed
    private const string Facts = $"""
        WITH sites AS ({DataLayerTiles.SitesSql})
        SELECT s.id, s.name,
               CASE
                   WHEN p.items IS NULL OR p.items = 0 THEN 'none'
                   WHEN p.done = 0 THEN 'pending'
                   WHEN p.done >= p.items THEN 'complete'
                   ELSE 'partial'
               END AS status,
               s.path_text, s.cell, s.location
        FROM sites s
        LEFT JOIN LATERAL (
            SELECT sum(cardinality(t.items)) AS items,
                   sum((SELECT count(*) FROM checklists.item_checks c
                         WHERE c.template_id = t.id AND c.site_id = s.id
                           AND c.business_date = (now() AT TIME ZONE s.time_zone)::date)) AS done
            FROM checklists.templates t
            WHERE t.org_id = @org AND (t.scope_path IS NULL OR s.path <@ t.scope_path::ltree)
        ) p ON true
        """;

    public Task<byte[]> RenderAsync(DataLayerTileRequest request, CancellationToken ct = default) =>
        DataLayerTiles.RenderAsync(db.Database, Name, MinPointZoom, Facts, request, ct: ct);
}
