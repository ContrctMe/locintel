using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Patrols.Patrols;

/// <summary>
/// One run of a route - a FACT row: stamps the site's ancestor path and
/// business date (ADR 2/26) so the daily activity report is a group-by.
/// ScheduledStartLocal ties it to an expected occurrence when it was one.
/// Tier 1 (status). StartedAt / EndedAt are UTC instants.
/// </summary>
public sealed class Patrol : IPathScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid RouteId { get; init; }
    public required Guid SiteId { get; init; }
    public required LTree Path { get; init; }
    public required DateOnly BusinessDate { get; init; }
    public TimeOnly? ScheduledStartLocal { get; init; }
    public PatrolStatus Status { get; set; } = PatrolStatus.InProgress;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public required Guid StartedBy { get; init; }
    public required string StartedByTier { get; init; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Summary { get; set; }
}
