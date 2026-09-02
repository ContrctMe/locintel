namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record RequestEventCreated(
    Guid Id,
    double? DistanceFromSiteMeters,
    bool? WithinGeofence
);
