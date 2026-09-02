using LocIntel.Modules.Alerts.Alerts;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Alerts.Alerts;

/// <summary>
/// One item in the org's alert feed, system-generated: a high-severity
/// incident (from the IncidentReported event) or a bulletin being issued.
/// Path is the stamped ancestor path of what it is about (ADR 2), or null
/// for org-wide; readers see alerts whose path lies in their scope. Reads
/// are per user (AlertRead). Kept 90 days by convention; no purge job yet.
/// CreatedAt is a UTC instant.
/// </summary>
public sealed class Alert : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required AlertKind Kind { get; init; }
    public required AlertSeverity Severity { get; init; }
    public required string Title { get; init; }
    public string Body { get; init; } = "";
    public LTree? Path { get; init; }
    public Guid? IncidentId { get; init; }
    public Guid? BulletinId { get; init; }
    public Guid? EntityId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
