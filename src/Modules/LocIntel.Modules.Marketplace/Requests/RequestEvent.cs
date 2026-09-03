using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Messages;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// One party's COPY of a timeline entry (ADR 48): the writer appends its own
/// row and fans RequestEventAppended out to the other party, whose handler
/// inserts its copy. SourceId is the entry's identity across copies (the
/// dedupe key on redelivery); Id is this copy's. Append-only. At is a UTC
/// instant.
/// </summary>
public sealed class RequestEvent : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid SourceId { get; init; }
    public required Guid RequestId { get; init; }
    public required OrgId ActorOrgId { get; init; }
    public required Guid ActorId { get; init; }
    public required RequestEventKind Kind { get; init; }
    public string? Body { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? DistanceFromSiteMeters { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    public RequestEventAppended Appended() =>
        new(
            RequestId,
            SourceId,
            ActorOrgId,
            ActorId,
            Kind,
            Body,
            Latitude,
            Longitude,
            DistanceFromSiteMeters,
            At
        );

    public static RequestEvent Copy(OrgId holder, RequestEventAppended m) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = holder,
            SourceId = m.SourceId,
            RequestId = m.RequestId,
            ActorOrgId = m.ActorOrgId,
            ActorId = m.ActorId,
            Kind = m.Kind,
            Body = m.Body,
            Latitude = m.Latitude,
            Longitude = m.Longitude,
            DistanceFromSiteMeters = m.DistanceFromSiteMeters,
            At = m.At,
        };
}
