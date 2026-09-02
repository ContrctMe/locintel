namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseIncidentView(
    Guid Id,
    Guid IncidentId,
    Guid SiteId,
    string? Title,
    string? Category,
    string? Status,
    DateTimeOffset? OccurredAt,
    DateTimeOffset AddedAt
);
