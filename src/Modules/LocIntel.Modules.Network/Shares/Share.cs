using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Shares;

/// <summary>
/// A consortium of orgs that share intelligence (blueprint module 9). The
/// OWNER'S aggregate (ADR 48): owned and read only by the owner org; every
/// member holds its own ShareAccess row, a projection kept current by
/// ShareRosterChanged. Deletion tier 1 (closed).
/// </summary>
public sealed class Share : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string OwnerName { get; init; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public ShareStatus Status { get; set; } = ShareStatus.Active;
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
