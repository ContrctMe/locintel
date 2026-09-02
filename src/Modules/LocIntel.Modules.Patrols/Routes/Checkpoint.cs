namespace LocIntel.Modules.Patrols.Routes;

/// <summary>One stop on a route: a short code the guard scans or taps, optionally geofenced.</summary>
public sealed record Checkpoint(
    string Code,
    string Label,
    double? Latitude = null,
    double? Longitude = null
);
