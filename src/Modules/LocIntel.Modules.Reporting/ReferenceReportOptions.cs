using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocIntel.Modules.Reporting;

/// <summary>Shared options for the two reference reports, not a general form language.</summary>
public sealed record ReferenceReportOptions
{
    public string? MapProvider { get; init; }
    public int MapZoom { get; init; } = 14;
    public Guid[] OverlayIds { get; init; } = [];
    public Dictionary<Guid, Guid[]> Photos { get; init; } = [];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ReferenceReportOptions Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException("Report options must be an object.");
        var result =
            value.Deserialize<ReferenceReportOptions>(Json)
            ?? throw new JsonException("Report options are required.");
        if (result.MapZoom is < 0 or > 18 || result.MapProvider?.Length > 80)
            throw new JsonException(
                "Map zoom must be 0..18 and provider ID at most 80 characters."
            );
        if (
            result.OverlayIds is null
            || result.OverlayIds.Length > 10
            || result.OverlayIds.Any(x => x == Guid.Empty)
            || result.OverlayIds.Distinct().Count() != result.OverlayIds.Length
        )
            throw new JsonException("Choose at most ten distinct overlays.");
        if (
            result.Photos is null
            || result.Photos.Count > 1000
            || result.Photos.Keys.Any(x => x == Guid.Empty)
            || result.Photos.Values.Any(x =>
                x is null
                || x.Length > 10
                || x.Any(id => id == Guid.Empty)
                || x.Distinct().Count() != x.Length
            )
            || result.Photos.Values.Sum(x => x.Length) > 1000
        )
            throw new JsonException(
                "Choose at most ten distinct photographs per site and 1,000 per request."
            );
        return result;
    }

    public Guid[] PhotosFor(IEnumerable<Guid> sites) =>
        sites.SelectMany(id => Photos.GetValueOrDefault(id) ?? []).Distinct().ToArray();

    public static string? Validate(JsonElement value, Rendering.ReportBasemaps? basemaps = null)
    {
        try
        {
            var options = Parse(value);
            if (
                options.MapProvider is not null
                && basemaps is not null
                && basemaps.Find(options.MapProvider) is null
            )
                return "Choose a configured report basemap.";
            return null;
        }
        catch (JsonException e)
        {
            return e.Message;
        }
    }
}
