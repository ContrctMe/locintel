using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace LocIntel.Modules.Spatial.Overlays;

/// <summary>
/// GeoJSON in, NetTopologySuite out (ADR 50: spatial types in application
/// code are NTS types; nobody hand-writes WKT). One reader, one set of
/// rules: a FeatureCollection or a single Feature or a bare geometry is
/// accepted, every geometry must be valid and in WGS84, and the first
/// version takes polygons only - the ADR's scope for overlay editing.
/// </summary>
public static class GeoJsonFeatures
{
    public const int MaxFeatures = 5000;
    public const int MaxVertices = 20_000;
    public const int MaxRings = 2_000;
    public const int MaxBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new GeoJsonConverterFactory(new GeometryFactory(new PrecisionModel(), 4326)),
        },
    };

    public sealed record Parsed(Geometry Geometry, string? Properties);

    /// <summary>Parses the upload; the error, when there is one, is the reason a 400 carries.</summary>
    public static (IReadOnlyList<Parsed> Features, string? Error) Parse(JsonElement geoJson)
    {
        if (geoJson.ValueKind != JsonValueKind.Object)
            return ([], "a GeoJSON object is required");
        if (!geoJson.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String)
            return ([], "GeoJSON type must be a string");
        var type = t.GetString();
        try
        {
            var raw = geoJson.GetRawText();
            if (System.Text.Encoding.UTF8.GetByteCount(raw) > MaxBytes)
                return ([], "GeoJSON exceeds the 2 MiB byte limit");
            if (!WithinBudget(geoJson))
                return ([], "GeoJSON exceeds feature, coordinate, ring or property limits");
            IEnumerable<IFeature> features = type switch
            {
                "FeatureCollection" => JsonSerializer.Deserialize<FeatureCollection>(raw, Options)
                    ?? [],
                "Feature" => [JsonSerializer.Deserialize<Feature>(raw, Options)!],
                null => [],
                _ => [new Feature(JsonSerializer.Deserialize<Geometry>(raw, Options), null)],
            };
            var parsed = new List<Parsed>();
            foreach (var feature in features)
            {
                if (parsed.Count >= MaxFeatures)
                    return ([], $"at most {MaxFeatures} features per layer");
                var geometry = feature.Geometry;
                if (geometry is null || geometry.IsEmpty)
                    return ([], "every feature needs a geometry");
                if (geometry is not (Polygon or MultiPolygon))
                    return ([], "overlays take polygons (Polygon or MultiPolygon) only");
                if (!geometry.IsValid)
                    return ([], "a polygon is not valid (self-intersecting or unclosed)");
                if (!InWgs84(geometry))
                    return ([], "coordinates must be longitude/latitude in WGS84");
                geometry.SRID = 4326;
                parsed.Add(new Parsed(geometry, PropertiesJson(feature.Attributes)));
            }
            return parsed.Count == 0 ? ([], "no features found") : (parsed, null);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException)
        {
            return ([], "malformed GeoJSON: " + e.Message);
        }
    }

    private static bool WithinBudget(JsonElement value)
    {
        var type = value.GetProperty("type").GetString();
        JsonElement[] features;
        if (type == "FeatureCollection")
        {
            if (
                !value.TryGetProperty("features", out var array)
                || array.ValueKind != JsonValueKind.Array
                || array.GetArrayLength() > MaxFeatures
            )
                return false;
            features = array.EnumerateArray().ToArray();
        }
        else
            features = [value];
        int points = 0,
            rings = 0;
        foreach (var feature in features)
        {
            if (feature.ValueKind != JsonValueKind.Object)
                return false;
            if (
                feature.TryGetProperty("properties", out var properties)
                && System.Text.Encoding.UTF8.GetByteCount(properties.GetRawText()) > 4096
            )
                return false;
            var geometry = feature.TryGetProperty("geometry", out var nested) ? nested : feature;
            if (
                geometry.ValueKind != JsonValueKind.Object
                || !geometry.TryGetProperty("coordinates", out var coordinates)
                || !CountCoordinates(coordinates, 0, ref points, ref rings)
            )
                return false;
        }
        return true;
    }

    private static bool CountCoordinates(
        JsonElement coordinates,
        int depth,
        ref int points,
        ref int rings
    )
    {
        if (
            depth > 4
            || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() == 0
        )
            return false;
        if (coordinates[0].ValueKind == JsonValueKind.Number)
            return coordinates.GetArrayLength() is 2 or 3
                && ++points <= MaxVertices
                && coordinates.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number);
        if (
            coordinates[0].ValueKind == JsonValueKind.Array
            && coordinates[0].GetArrayLength() > 0
            && coordinates[0][0].ValueKind == JsonValueKind.Number
            && ++rings > MaxRings
        )
            return false;
        foreach (var child in coordinates.EnumerateArray())
            if (!CountCoordinates(child, depth + 1, ref points, ref rings))
                return false;
        return true;
    }

    private static bool InWgs84(Geometry geometry)
    {
        var e = geometry.EnvelopeInternal;
        return e.MinX >= -180 && e.MaxX <= 180 && e.MinY >= -90 && e.MaxY <= 90;
    }

    private static string? PropertiesJson(IAttributesTable? attributes)
    {
        if (attributes is null || attributes.Count == 0)
            return null;
        return JsonSerializer.Serialize(attributes, Options);
    }
}
