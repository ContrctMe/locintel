namespace LocIntel.Contracts.Incidents;

/// <summary>
/// Integration event (ADR 23/37): Incidents publishes one per report with
/// the tenant on the envelope; modules above (Alerts) react. Path is the
/// stamped ancestor path so subscribers can scope without a lookup.
/// </summary>
public sealed record IncidentReported(
    Guid IncidentId,
    Guid SiteId,
    string SiteName,
    string Path,
    string Category,
    string Severity,
    string Title,
    DateTimeOffset OccurredAt,
    Guid ReportedBy
);
