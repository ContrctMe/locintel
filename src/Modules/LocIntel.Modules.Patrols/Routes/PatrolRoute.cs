using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Patrols.Routes;

/// <summary>
/// A tour of checkpoints at one site (blueprint module 8; the ops answer
/// to guard rounds). Path is the site's ancestor path stamped at creation
/// (ADR 2) so gate 3 is one ltree predicate. Deletion tier 1: archived.
/// </summary>
public sealed class PatrolRoute : IPathScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid SiteId { get; init; }
    public required LTree Path { get; init; }
    public required string Name { get; set; }

    /// <summary>Ordered checkpoints as JSON (jsonb): [{code,label,latitude,longitude}].</summary>
    public string CheckpointsJson { get; set; } = "[]";
    public int ExpectedMinutes { get; set; } = 30;
    public bool Archived { get; set; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
