using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

/// <summary>Direct-to-vendor (v1): the vendor is chosen up front; the request starts as a Draft.</summary>
public sealed record CreateRequest(
    Guid VendorOrgId,
    ServiceCategory Category,
    RequestUrgency Urgency,
    Guid SiteId,
    string Title,
    DateTimeOffset StartsAt,
    string? Details = null,
    Dictionary<string, string>? Spec = null,
    DateTimeOffset? EndsAt = null,
    string? Rrule = null,
    decimal? BudgetAmount = null,
    string? Currency = null,
    Guid? IncidentId = null,
    Guid? CaseId = null
);
