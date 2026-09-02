using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Patrols.Routes;

/// <summary>
/// When a route is expected to run: a wall-clock recurring rule in the
/// site's zone (ADR 26 kind 2 / ADR 27) with a local start time; expanded
/// on read for the day being looked at. Tier 3 (hard delete).
/// </summary>
public sealed class PatrolSchedule : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid RouteId { get; init; }
    public required Guid SiteId { get; init; }
    public required string RRule { get; set; }
    public required DateOnly AnchorDate { get; set; }
    public required TimeOnly StartLocal { get; set; }
    public DateOnly[] ExDates { get; set; } = [];
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
