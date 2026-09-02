namespace LocIntel.Modules.Patrols.Patrols.Api;

public sealed record IncidentLine(
    Guid Id,
    string Title,
    string Category,
    string Severity,
    string Status,
    DateTimeOffset OccurredAt
);
