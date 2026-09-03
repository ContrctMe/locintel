using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests.Messages;

/// <summary>A timeline entry for the other party to copy (keyed on SourceId in the recipient's tenant).</summary>
public sealed record RequestEventAppended(
    Guid RequestId,
    Guid SourceId,
    OrgId ActorOrgId,
    Guid ActorId,
    RequestEventKind Kind,
    string? Body,
    double? Latitude,
    double? Longitude,
    double? DistanceFromSiteMeters,
    DateTimeOffset At
);
