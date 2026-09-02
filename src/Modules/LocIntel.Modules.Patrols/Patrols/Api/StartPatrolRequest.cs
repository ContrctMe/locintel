namespace LocIntel.Modules.Patrols.Patrols.Api;

/// <summary>ScheduledStartLocal ties the run to an expected occurrence; omit for an unscheduled round.</summary>
public sealed record StartPatrolRequest(Guid RouteId, TimeOnly? ScheduledStartLocal = null);
