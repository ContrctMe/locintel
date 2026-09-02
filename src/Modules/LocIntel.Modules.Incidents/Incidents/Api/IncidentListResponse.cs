namespace LocIntel.Modules.Incidents.Incidents.Api;

public sealed record IncidentListResponse(
    IReadOnlyList<IncidentSummary> Items,
    int Total,
    int? NextOffset
);
