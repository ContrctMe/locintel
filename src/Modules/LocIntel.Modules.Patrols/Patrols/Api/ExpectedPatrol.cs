namespace LocIntel.Modules.Patrols.Patrols.Api;

/// <summary>An occurrence the schedules expect on this day, and the run that matched it (if any).</summary>
public sealed record ExpectedPatrol(
    Guid RouteId,
    string RouteName,
    TimeOnly StartLocal,
    DateTimeOffset StartUtc,
    Guid? PatrolId,
    PatrolStatus? Status
);
