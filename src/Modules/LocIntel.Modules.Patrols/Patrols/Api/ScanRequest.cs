namespace LocIntel.Modules.Patrols.Patrols.Api;

public sealed record ScanRequest(
    string Code,
    double? Latitude = null,
    double? Longitude = null,
    string? Note = null
);
