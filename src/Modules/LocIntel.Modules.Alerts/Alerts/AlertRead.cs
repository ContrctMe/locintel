using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Alerts.Alerts;

/// <summary>A user has seen an alert. Tier 3. ReadAt is a UTC instant.</summary>
public sealed class AlertRead : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid AlertId { get; init; }
    public required Guid UserId { get; init; }
    public DateTimeOffset ReadAt { get; init; } = DateTimeOffset.UtcNow;
}
