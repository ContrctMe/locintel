namespace LocIntel.Modules.Patrols.Patrols.Api;

public sealed record PatrolView(
    Guid Id,
    Guid RouteId,
    string RouteName,
    Guid SiteId,
    DateOnly BusinessDate,
    TimeOnly? ScheduledStartLocal,
    PatrolStatus Status,
    DateTimeOffset StartedAt,
    Guid StartedBy,
    string? StartedByLabel,
    DateTimeOffset? EndedAt,
    string? Summary,
    int CheckpointsTotal,
    int CheckpointsScanned,
    IReadOnlyList<ScanView> Scans
);
