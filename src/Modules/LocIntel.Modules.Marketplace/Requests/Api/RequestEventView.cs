using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

/// <summary>Side says which party acted ("requester" | "vendor"); actor labels are resolved only for your own org.</summary>
public sealed record RequestEventView(
    Guid Id,
    string Side,
    Guid ActorId,
    string? Actor,
    RequestEventKind Kind,
    string? Body,
    double? Latitude,
    double? Longitude,
    double? DistanceFromSiteMeters,
    bool? WithinGeofence,
    DateTimeOffset At
);
