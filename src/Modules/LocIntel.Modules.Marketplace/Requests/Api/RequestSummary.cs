using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record RequestSummary(
    Guid Id,
    Guid VendorOrgId,
    string? VendorName,
    string RequesterName,
    ServiceCategory Category,
    RequestUrgency Urgency,
    RequestStatus Status,
    Guid SiteId,
    string SiteName,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    decimal? BudgetAmount,
    string Currency,
    DateTimeOffset UpdatedAt
);
