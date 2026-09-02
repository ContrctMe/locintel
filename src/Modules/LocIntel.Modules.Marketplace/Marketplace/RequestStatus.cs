using System.Text.Json.Serialization;

namespace LocIntel.Modules.Marketplace.Marketplace;

/// <summary>
/// The lifecycle (deletion tier 1: a request is cancelled, never deleted).
/// Draft > Submitted > Accepted | Declined; Accepted > InProgress > Completed
/// > Verified | Disputed (> Verified). Cancelled from Draft/Submitted/Accepted.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestStatus
{
    Draft,
    Submitted,
    Accepted,
    Declined,
    InProgress,
    Completed,
    Verified,
    Disputed,
    Cancelled,
}
