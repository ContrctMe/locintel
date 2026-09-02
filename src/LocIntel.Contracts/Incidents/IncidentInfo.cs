namespace LocIntel.Contracts.Incidents;

/// <summary>
/// What a module above Incidents may know about an incident (ADR 37): enough
/// to link to it and to apply gate 3 (Path) without reading its table.
/// </summary>
public sealed record IncidentInfo(
    Guid Id,
    Guid SiteId,
    string Path,
    string Title,
    string Category,
    string Severity,
    string Status,
    DateTimeOffset OccurredAt
);
