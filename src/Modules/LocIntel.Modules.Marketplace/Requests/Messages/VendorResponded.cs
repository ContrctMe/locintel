using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests.Messages;

/// <summary>
/// The vendor acted on its own assignment; sent to the requester, whose
/// handler arbitrates against the request's current state and answers with
/// RequestStateChanged. SourceId is the timeline entry both sides keep.
/// </summary>
public sealed record VendorResponded(
    Guid RequestId,
    OrgId VendorOrgId,
    string VendorName,
    VendorResponse Response,
    string? Text,
    Guid ActorId,
    Guid SourceId,
    DateTimeOffset At
);
