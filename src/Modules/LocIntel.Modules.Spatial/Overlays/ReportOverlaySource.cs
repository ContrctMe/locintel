using LocIntel.Contracts;
using LocIntel.Modules.Spatial.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocIntel.Modules.Spatial.Overlays;

public sealed class ReportOverlaySource(SpatialDbContext db) : IReportOverlaySource
{
    public async Task<IReadOnlyList<IReportOverlaySource.Layer>> ReadAsync(
        OrgId org,
        NodeScope scope,
        Guid[] ids,
        CancellationToken ct = default
    )
    {
        if (ids.Length > 10)
            throw new ArgumentOutOfRangeException(nameof(ids), "At most ten report overlays.");
        // Keep the size check and geometry materialization on the same snapshot.
        // This source owns only a short read transaction, never rendering or HTTP work.
        await using var snapshot = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead,
            ct
        );
        var layers = await db
            .Layers.AsNoTracking()
            .Where(x => x.OrgId == org && ids.Contains(x.Id))
            .InScope(scope)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        var result = new List<IReportOverlaySource.Layer>();
        var vertices = 0;
        foreach (var layer in layers)
        {
            // Bound before materializing geometry bytes; an over-budget layer is an explicit failure.
            var points = await db
                .Database.SqlQuery<long>(
                    $"""
                    SELECT COALESCE(SUM(ST_NPoints(geom::geometry)), 0)::bigint AS "Value"
                    FROM spatial.overlay_features
                    WHERE org_id = {org.Value} AND layer_id = {layer.Id}
                    """
                )
                .SingleAsync(ct);
            if (points + vertices > 20000)
                throw new InvalidOperationException("Report overlay vertex limit exceeded.");
            var features = await db
                .Features.AsNoTracking()
                .Where(x => x.OrgId == org && x.LayerId == layer.Id)
                .InScope(scope)
                .Select(x => x.Geom)
                .ToListAsync(ct);
            var polygons = new List<double[][][]>();
            foreach (var geometry in features)
            {
                foreach (
                    var polygon in Enumerable
                        .Range(0, geometry.NumGeometries)
                        .Select(i => (Polygon)geometry.GetGeometryN(i))
                )
                {
                    var rings = new[] { polygon.ExteriorRing }.Concat(polygon.InteriorRings);
                    polygons.Add(
                        rings
                            .Select(ring =>
                                ring.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray()
                            )
                            .ToArray()
                    );
                    vertices += polygon.NumPoints;
                }
            }
            result.Add(new(layer.Id, layer.Name, layer.Style, polygons.ToArray()));
        }
        return result;
    }
}
