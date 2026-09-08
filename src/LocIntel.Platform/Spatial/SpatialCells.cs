namespace LocIntel.Platform.Spatial;

/// <summary>
/// The spatial key (ADR 51): every point's Web Mercator tile at zoom
/// <see cref="Zoom"/>, interleaved (Morton / Z-order) into one bigint. A
/// tile at any lower zoom is then ONE contiguous key range, and a viewport
/// is a handful of them - integer comparisons, which are leakproof, so a
/// btree on <c>(org_id, cell)</c> serves the app role under row security
/// where the geography GiST index cannot (its operators are not leakproof).
/// The geography column stays for exactness and distance; the key only
/// narrows.
/// </summary>
public static class SpatialCells
{
    /// <summary>Cell resolution: 2^20 tiles per axis, about 40 m at the equator.</summary>
    public const int Zoom = 20;

    /// <summary>Web Mercator's latitude limit; anything beyond is clamped into the edge cell.</summary>
    public const double MaxLatitude = 85.05112878;

    /// <summary>The key of a point, or null when either coordinate is missing.</summary>
    public static long? Key(double? latitude, double? longitude)
    {
        if (latitude is not { } lat || longitude is not { } lon)
            return null;
        if (!double.IsFinite(lat) || !double.IsFinite(lon))
            return null;
        var (x, y) = Tile(lat, lon, Zoom);
        return Interleave(x, y);
    }

    /// <summary>The slippy-map tile a point falls in at zoom <paramref name="z"/>, clamped to the grid.</summary>
    public static (int x, int y) Tile(double latitude, double longitude, int z)
    {
        var n = 1 << z;
        var lat = Math.Clamp(latitude, -MaxLatitude, MaxLatitude);
        var x = (int)Math.Floor((longitude + 180) / 360 * n);
        var rad = lat * Math.PI / 180;
        var y = (int)
            Math.Floor((1 - Math.Log(Math.Tan(rad) + 1 / Math.Cos(rad)) / Math.PI) / 2 * n);
        return (Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1));
    }

    /// <summary>The key range [lo, hi) of everything inside tile z/x/y.</summary>
    public static (long lo, long hi) TileRange(int z, int x, int y)
    {
        if (z <= Zoom)
        {
            var shift = 2 * (Zoom - z);
            var key = Interleave(x, y);
            return (key << shift, (key + 1) << shift);
        }
        // finer than a cell: the one cell the tile sits in (callers add an exact test)
        var d = z - Zoom;
        var cell = Interleave(x >> d, y >> d);
        return (cell, cell + 1);
    }

    /// <summary>
    /// Key ranges covering a viewport: the tiles of the finest zoom at which
    /// at most <paramref name="maxTiles"/> tiles cover the box, merged where
    /// adjacent. A superset - callers narrow with the exact coordinates.
    /// </summary>
    public static IReadOnlyList<(long lo, long hi)> Cover(BoundingBox box, int maxTiles = 16)
    {
        IReadOnlyList<(double west, double east)> longitudes = box.CrossesAntimeridian
            ? [(box.West, 180), (-180, box.East)]
            : [(box.West, box.East)];
        var chosen = 0;
        for (var z = Zoom; z >= 0; z--)
        {
            long count = 0;
            foreach (var (west, east) in longitudes)
            {
                var (x0, y0) = Tile(box.North, west, z);
                var (x1, y1) = Tile(box.South, east, z);
                count += (long)(x1 - x0 + 1) * (y1 - y0 + 1);
            }
            if (count <= maxTiles)
            {
                chosen = z;
                break;
            }
        }
        var ranges = new List<(long lo, long hi)>();
        foreach (var (west, east) in longitudes)
        {
            var (x0, y0) = Tile(box.North, west, chosen);
            var (x1, y1) = Tile(box.South, east, chosen);
            for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++)
                ranges.Add(TileRange(chosen, x, y));
        }
        ranges.Sort();
        var merged = new List<(long lo, long hi)>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && merged[^1].hi >= range.lo)
                merged[^1] = (merged[^1].lo, Math.Max(merged[^1].hi, range.hi));
            else
                merged.Add(range);
        }
        return merged;
    }

    /// <summary>Morton code: x's bits in the even positions, y's in the odd.</summary>
    public static long Interleave(long x, long y) => Spread(x) | (Spread(y) << 1);

    private static long Spread(long v)
    {
        v &= 0xFFFFFFFFL;
        v = (v | (v << 16)) & 0x0000FFFF0000FFFFL;
        v = (v | (v << 8)) & 0x00FF00FF00FF00FFL;
        v = (v | (v << 4)) & 0x0F0F0F0F0F0F0F0FL;
        v = (v | (v << 2)) & 0x3333333333333333L;
        v = (v | (v << 1)) & 0x5555555555555555L;
        return v;
    }
}
