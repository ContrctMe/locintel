using NetTopologySuite.Geometries;

namespace LocIntel.Platform.Spatial;

/// <summary>
/// A map viewport as the client sends it (ADR 49): WGS84 degrees,
/// west,south,east,north. Pure value: parsing, validation, and the split into
/// envelopes live here so an endpoint only asks for the pieces to intersect
/// with. A box with <c>West &gt; East</c> crosses the antimeridian and is cut
/// there; any span wider than 90° is cut again, because a geography polygon
/// edge of 180° (antipodal endpoints) has no defined great circle and PostGIS
/// refuses it - a planet-sized box at zoom 0 is a legitimate request.
/// </summary>
public readonly record struct BoundingBox(double West, double South, double East, double North)
{
    /// <summary>Longitude/latitude order is the GeoJSON one: x = longitude.</summary>
    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);

    /// <summary>Longest span a single envelope may cover, on either axis.</summary>
    public const double MaxSpanDegrees = 90;

    public bool CrossesAntimeridian => West > East;

    /// <summary>
    /// Parse the query form. Returns an error message, never throws: a
    /// malformed box is a 400 the endpoint phrases, not an exception.
    /// </summary>
    public static bool TryParse(string? text, out BoundingBox box, out string? error)
    {
        box = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "bbox is empty";
            return false;
        }
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            error = "bbox must be west,south,east,north";
            return false;
        }
        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (
                !double.TryParse(
                    parts[i],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out values[i]
                ) || !double.IsFinite(values[i])
            )
            {
                error = "bbox must be four finite numbers";
                return false;
            }
        }
        var candidate = new BoundingBox(values[0], values[1], values[2], values[3]);
        if (Math.Abs(candidate.West) > 180 || Math.Abs(candidate.East) > 180)
        {
            error = "bbox longitude out of range";
            return false;
        }
        if (Math.Abs(candidate.South) > 90 || Math.Abs(candidate.North) > 90)
        {
            error = "bbox latitude out of range";
            return false;
        }
        if (candidate.South > candidate.North)
        {
            error = "bbox south must not exceed north";
            return false;
        }
        box = candidate;
        return true;
    }

    /// <summary>
    /// The envelopes to intersect with, each at most <see cref="MaxSpanDegrees"/>
    /// wide and tall: one for an ordinary viewport, two across the antimeridian,
    /// up to eight for the whole planet. Polygons in SRID 4326 so the provider
    /// passes them as spatial parameters.
    /// </summary>
    public IReadOnlyList<Geometry> Envelopes()
    {
        IEnumerable<(double west, double east)> longitudes = CrossesAntimeridian
            ? [(West, 180), (-180, East)]
            : [(West, East)];
        var result = new List<Geometry>();
        foreach (var (west, east) in longitudes)
        foreach (var (w, e) in Split(west, east))
        foreach (var (s, n) in Split(South, North))
            result.Add(Wgs84.ToGeometry(new Envelope(w, e, s, n)));
        return result;
    }

    private static IEnumerable<(double from, double to)> Split(double from, double to)
    {
        var span = to - from;
        if (span <= MaxSpanDegrees)
        {
            yield return (from, to);
            yield break;
        }
        var pieces = (int)Math.Ceiling(span / MaxSpanDegrees);
        var step = span / pieces;
        for (var i = 0; i < pieces; i++)
            yield return (from + i * step, i == pieces - 1 ? to : from + (i + 1) * step);
    }
}
