namespace LocIntel.Modules.Incidents.Incidents.Api;

/// <summary>Rollups over the STAMPED business date and path (ADR 2/26): a group-by, never a per-row conversion.</summary>
public sealed record IncidentStatsResponse(
    DateOnly From,
    DateOnly To,
    int Total,
    int Open,
    decimal TotalLoss,
    IReadOnlyList<IncidentCount> ByCategory,
    IReadOnlyList<IncidentCount> BySeverity,
    IReadOnlyList<IncidentCount> BySite,
    IReadOnlyList<IncidentCount> ByBusinessDate,
    IReadOnlyList<IncidentCount> ByHour,
    IReadOnlyList<IncidentCount> ByWeekday
);
