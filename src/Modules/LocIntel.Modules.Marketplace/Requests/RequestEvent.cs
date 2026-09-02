using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The request's timeline, visible to both parties: messages, status
/// changes, guard check-in/out (with distance from the site when both
/// have coordinates), deliveries. Denormalizes both org ids for the
/// two-party policy. Append-only. At is a UTC instant.
/// </summary>
public sealed class RequestEvent : ITwoPartyScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }

    /// <summary>The vendor party, when the request has one (ITwoPartyScoped's counterparty).</summary>
    public OrgId? CounterpartyOrgId { get; init; }
    public required Guid RequestId { get; init; }
    public required OrgId ActorOrgId { get; init; }
    public required Guid ActorId { get; init; }
    public required RequestEventKind Kind { get; init; }
    public string? Body { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? DistanceFromSiteMeters { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
