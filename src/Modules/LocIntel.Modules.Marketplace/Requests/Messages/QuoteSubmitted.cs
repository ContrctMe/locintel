using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests.Messages;

/// <summary>The vendor's own quote, for the requester to project and compare.</summary>
public sealed record QuoteSubmitted(
    Guid RequestId,
    OrgId VendorOrgId,
    string VendorName,
    Guid QuoteId,
    decimal Amount,
    string Currency,
    string? Notes,
    DateTimeOffset? ValidUntil,
    DateTimeOffset SubmittedAt
);
