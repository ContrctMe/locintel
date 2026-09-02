using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record QuoteView(
    Guid Id,
    Guid VendorOrgId,
    string? VendorName,
    decimal Amount,
    string Currency,
    string? Notes,
    DateTimeOffset? ValidUntil,
    QuoteStatus Status,
    DateTimeOffset CreatedAt
);
