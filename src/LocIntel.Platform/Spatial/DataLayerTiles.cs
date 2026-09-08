using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace LocIntel.Platform.Spatial;

/// <summary>
/// The one MVT renderer every point data layer shares (ADR 50 §4): tile
/// envelope, scope predicates, clustering below the layer's point zoom,
/// <c>ST_AsMVTGeom</c>/<c>ST_AsMVT</c>. A layer supplies only its facts
/// query - the columns <c>id</c>, <c>name</c>, <c>status</c>,
/// <c>path_text</c>, <c>cell</c> and <c>location</c> (geography) - and this
/// does the rest, so the projection decision and the scope predicate live in
/// one place. The tile and the scope are both key RANGES (ADR 51): the tile
/// is one contiguous <c>cell</c> range and a subtree one contiguous
/// <c>path_text</c> range, so the app role reaches the btree indexes under
/// row security, which the geography and ltree operators cannot.
/// </summary>
public static class DataLayerTiles
{
    public const int Extent = 4096;
    public const int Buffer = 64;
    public const int MaxZoom = 22;

    /// <summary>
    /// The site facts every layer starts from, as SQL: the org's sites that
    /// have a location. Named here, below every module, so a layer in any
    /// module joins its own facts to <c>s.id</c> without spelling Tenancy's
    /// schema - the same way the audit sink is one shared, known table.
    /// Yields <c>id, name, status, path, path_text, cell, time_zone,
    /// location</c>; expects <c>@org</c>. A layer's own facts query must
    /// keep the filter on <c>s</c> reachable (join to it, never aggregate
    /// over it first), so the tile's cell range reaches the sites index.
    /// </summary>
    public const string SitesSql = """
        SELECT s.id, s.name, s.status, s.path, s.path_text, s.cell, s.time_zone, s.location
        FROM tenancy.sites s
        WHERE s.org_id = @org AND s.cell IS NOT NULL
        """;

    public static bool InRange(int z, int x, int y) =>
        z is >= 0 and <= MaxZoom && x >= 0 && y >= 0 && x < (1L << z) && y < (1L << z);

    /// <summary>
    /// Renders one tile. <paramref name="factsSql"/> may use <c>@org</c> and
    /// any parameter in <paramref name="extra"/>; the names <c>z</c>,
    /// <c>x</c>, <c>y</c>, <c>lo</c>, <c>hi</c> and anything starting with
    /// <c>scope</c> or <c>under</c> are the renderer's.
    /// </summary>
    public static async Task<byte[]> RenderAsync(
        DatabaseFacade database,
        string layerName,
        int minPointZoom,
        string factsSql,
        DataLayerTileRequest request,
        IEnumerable<NpgsqlParameter>? extra = null,
        CancellationToken ct = default
    )
    {
        var (lo, hi) = SpatialCells.TileRange(request.Z, request.X, request.Y);
        var parameters = new List<NpgsqlParameter>
        {
            new("z", request.Z),
            new("x", request.X),
            new("y", request.Y),
            new("org", request.Org.Value),
            new("lo", lo),
            new("hi", hi),
        };
        if (extra is not null)
            parameters.AddRange(extra);

        // the tile as a key range: the btree's job. Finer than a cell, the
        // range is the enclosing cell and the envelope makes it exact - a tile
        // that small is far from 180° wide, so the geography test is safe.
        var predicates = " AND f.cell >= @lo AND f.cell < @hi";
        if (request.Z > SpatialCells.Zoom)
            predicates +=
                " AND ST_Intersects(f.location, ST_Transform(ST_TileEnvelope(@z, @x, @y), 4326)::geography)";
        if (request.ScopePaths is { } paths)
            predicates += " AND " + PathKeys.Sql("f.path_text", paths, "scope", parameters);
        if (request.UnderPath is { } under)
            predicates += " AND " + PathKeys.Sql("f.path_text", [under], "under", parameters);

        // features: single points, or clusters on a grid of one eighth of the
        // tile. A cluster grid cell is the tile three zooms down, so grouping is
        // an integer shift of the key and a mean of the coordinates - no
        // geometry work per point, only per cluster; a world tile of a million
        // sites is one pass over two doubles and a bigint.
        var clusterShift = 2 * (SpatialCells.Zoom - Math.Min(request.Z + 3, SpatialCells.Zoom));
        parameters.Add(new NpgsqlParameter("shift", clusterShift));
        var features =
            request.Z >= minPointZoom
                ? $"""
                    SELECT ST_AsMVTGeom(ST_Transform(i.geom, 3857), b.geom, {Extent}, {Buffer}, true) AS geom,
                           i.id::text AS id, i.name, i.status, 1 AS count
                    FROM inside i, bounds b
                    """
                : $"""
                    SELECT ST_AsMVTGeom(ST_Transform(ST_SetSRID(ST_MakePoint(avg(ST_X(i.geom)), avg(ST_Y(i.geom))), 4326), 3857), b.geom, {Extent}, {Buffer}, true) AS geom,
                           NULL::text AS id, NULL::text AS name, NULL::text AS status, count(*)::int AS count
                    FROM inside i, bounds b
                    GROUP BY b.geom, (i.cell >> @shift)
                    """;

        var sql = $"""
            WITH bounds AS (SELECT ST_TileEnvelope(@z, @x, @y) AS geom),
            facts AS ({factsSql}),
            inside AS (
                SELECT f.id, f.name, f.status::text AS status, f.cell, f.location::geometry AS geom
                FROM facts f
                WHERE true{predicates}
            ),
            features AS ({features})
            SELECT COALESCE(ST_AsMVT(f, '{layerName}', {Extent}, 'geom'), ''::bytea) AS "Value"
            FROM features f
            WHERE f.geom IS NOT NULL
            """;

        return await database
            .SqlQueryRaw<byte[]>(sql, parameters.Cast<object>().ToArray())
            .SingleAsync(ct);
    }
}
