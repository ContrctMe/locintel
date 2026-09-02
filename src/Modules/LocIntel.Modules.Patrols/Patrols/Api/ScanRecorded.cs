namespace LocIntel.Modules.Patrols.Patrols.Api;

public sealed record ScanRecorded(
    Guid Id,
    double? DistanceMeters,
    bool? WithinGeofence,
    int Scanned,
    int Total
);
