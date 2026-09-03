using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The requester's request as its participants may see it (ADR 48: the
/// event carries what recipients are allowed to know). Rides RequestOffered
/// and RequestStateChanged; a vendor's assignment row applies it whole, so
/// the requester's state is always authoritative on the vendor's side.
/// </summary>
public sealed record RequestSnapshot(
    Guid RequestId,
    OrgId RequesterOrgId,
    string RequesterName,
    OrgId? VendorOrgId,
    RequestMode Mode,
    ServiceCategory Category,
    RequestUrgency Urgency,
    RequestStatus Status,
    Guid SiteId,
    string SiteName,
    string SiteTimeZone,
    double? SiteLatitude,
    double? SiteLongitude,
    string? SiteCountryCode,
    string Title,
    string Details,
    string SpecJson,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string? Rrule,
    decimal? BudgetAmount,
    string Currency,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ResponseDueAt,
    DateTimeOffset? EscalatedAt,
    int EscalationCount,
    DateTimeOffset? AcceptedAt,
    string? DeclineReason,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? CompletionSummary,
    DateTimeOffset? VerifiedAt,
    string? DisputeReason,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    DateTimeOffset UpdatedAt
);
