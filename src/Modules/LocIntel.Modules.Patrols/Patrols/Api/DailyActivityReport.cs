namespace LocIntel.Modules.Patrols.Patrols.Api;

/// <summary>The day at one site on its own clock: what was expected, what ran, what was missed, and what happened.</summary>
public sealed record DailyActivityReport(
    DateOnly BusinessDate,
    Guid SiteId,
    string Site,
    int Expected,
    int Completed,
    IReadOnlyList<ExpectedPatrol> Missed,
    IReadOnlyList<PatrolView> Patrols,
    IReadOnlyList<IncidentLine> Incidents
);
