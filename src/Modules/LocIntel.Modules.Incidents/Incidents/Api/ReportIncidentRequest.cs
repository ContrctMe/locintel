namespace LocIntel.Modules.Incidents.Incidents.Api;

/// <summary>OccurredAt is a UTC instant; the server stamps the site-local business date.</summary>
public sealed record ReportIncidentRequest(
    Guid SiteId,
    IncidentCategory Category,
    IncidentSeverity Severity,
    string Title,
    DateTimeOffset OccurredAt,
    string? Narrative = null,
    string? LocationDetail = null,
    decimal? LossAmount = null,
    decimal? RecoveredAmount = null,
    string? Currency = null,
    string? PoliceReportNumber = null,
    string[]? Tags = null
);
