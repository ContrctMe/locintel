using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

/// <summary>Drafts only; once submitted the vendor is looking at it.</summary>
public sealed record UpdateRequest(
    RequestUrgency Urgency,
    string Title,
    DateTimeOffset StartsAt,
    string? Details,
    Dictionary<string, string>? Spec,
    DateTimeOffset? EndsAt,
    string? Rrule,
    decimal? BudgetAmount,
    string? Currency
);
