using LocIntel.Modules.Alerts.Alerts;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Alerts.Bulletins;

/// <summary>
/// A BOLO, advisory, or safety notice pushed to sites. ScopePath targets a
/// subtree (null = the whole org); readers whose scope overlaps see it.
/// Links to an entity, incident, or case are by id (the reader's own
/// need-to-know applies when they follow them). Deletion tier 1: withdrawn.
/// IssuedAt / ExpiresAt / WithdrawnAt are UTC instants.
/// </summary>
public sealed class Bulletin : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required BulletinKind Kind { get; init; }
    public required AlertSeverity Severity { get; init; }
    public required string Title { get; set; }
    public string Body { get; set; } = "";
    public LTree? ScopePath { get; init; }
    public Guid? EntityId { get; init; }
    public Guid? IncidentId { get; init; }
    public Guid? CaseId { get; init; }
    public required Guid IssuedBy { get; init; }
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;
    public required DateTimeOffset ExpiresAt { get; set; }
    public BulletinStatus Status { get; set; } = BulletinStatus.Active;
    public DateTimeOffset? WithdrawnAt { get; set; }
    public Guid? WithdrawnBy { get; set; }
}
