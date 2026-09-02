namespace LocIntel.Modules.Incidents.Incidents.Api;

public sealed record IncidentSummary(
    Guid Id,
    Guid SiteId,
    IncidentCategory Category,
    IncidentSeverity Severity,
    IncidentStatus Status,
    string Title,
    DateTimeOffset OccurredAt,
    DateOnly BusinessDate,
    decimal? LossAmount,
    bool LegalHold,
    DateTimeOffset? DeletedAt
);
