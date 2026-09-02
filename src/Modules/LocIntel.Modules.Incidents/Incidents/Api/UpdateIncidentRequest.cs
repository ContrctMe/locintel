namespace LocIntel.Modules.Incidents.Incidents.Api;

/// <summary>Full replacement of the editable fields; the site and the stamps never move.</summary>
public sealed record UpdateIncidentRequest(
    IncidentCategory Category,
    IncidentSeverity Severity,
    string Title,
    DateTimeOffset OccurredAt,
    string? Narrative,
    string? LocationDetail,
    decimal? LossAmount,
    decimal? RecoveredAmount,
    string? Currency,
    string? PoliceReportNumber,
    string[]? Tags
);
