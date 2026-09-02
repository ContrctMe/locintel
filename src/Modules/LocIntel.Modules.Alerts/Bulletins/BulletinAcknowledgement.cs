using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Alerts.Bulletins;

/// <summary>"Seen and briefed" at a site. Tier 3. AcknowledgedAt is a UTC instant.</summary>
public sealed class BulletinAcknowledgement : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid BulletinId { get; init; }
    public required Guid UserId { get; init; }
    public Guid? SiteId { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset AcknowledgedAt { get; init; } = DateTimeOffset.UtcNow;
}
