using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// Entity-to-incident edge: the graph that shows one crew hitting six
/// stores. Stamps the incident's ancestor path at link time (ADR 2) so
/// need-to-know resolves inside this schema with one ltree predicate.
/// Join row: deletion tier 3 (hard). LinkedAt is a UTC instant.
/// </summary>
public sealed class EntityIncidentLink : IPathScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid EntityId { get; init; }
    public required Guid IncidentId { get; init; }
    public required Guid SiteId { get; init; }
    public required LTree Path { get; init; }
    public required LinkRole Role { get; set; }
    public string? Note { get; set; }
    public required Guid LinkedBy { get; init; }
    public DateTimeOffset LinkedAt { get; init; } = DateTimeOffset.UtcNow;
}
