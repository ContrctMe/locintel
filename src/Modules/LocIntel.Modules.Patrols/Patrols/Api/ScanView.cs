namespace LocIntel.Modules.Patrols.Patrols.Api;

public sealed record ScanView(
    Guid Id,
    string Code,
    string? Label,
    DateTimeOffset ScannedAt,
    double? DistanceMeters,
    bool? WithinGeofence,
    string? Note
);
