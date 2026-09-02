using LocIntel.Modules.Incidents.Incidents;

namespace LocIntel.Modules.Incidents.Imports.Api;

public sealed record ImportRowView(
    int RowNumber,
    string SiteRef,
    Guid? SiteId,
    IncidentCategory? Category,
    IncidentSeverity? Severity,
    DateTimeOffset? OccurredAt,
    string Title,
    decimal? LossAmount,
    string[] Errors,
    Guid? IncidentId
);
