using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

/// <summary>Direct (VendorOrgId set) or Broadcast (VendorOrgId null: quotes from every matching vendor). Starts as a Draft.</summary>
public sealed record CreateRequest(
    ServiceCategory Category,
    RequestUrgency Urgency,
    Guid SiteId,
    string Title,
    DateTimeOffset StartsAt,
    Guid? VendorOrgId = null,
    string? Details = null,
    Dictionary<string, string>? Spec = null,
    DateTimeOffset? EndsAt = null,
    string? Rrule = null,
    decimal? BudgetAmount = null,
    string? Currency = null,
    Guid? IncidentId = null,
    Guid? CaseId = null
);
