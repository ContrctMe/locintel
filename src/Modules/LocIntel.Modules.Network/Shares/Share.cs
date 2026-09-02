using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Shares;

/// <summary>
/// A consortium of orgs that share intelligence (blueprint module 9). Owned
/// by one org; visible to every org holding a ShareAccess row. Not
/// IOrgScoped - the context filters it through the caller's own access
/// rows, mirrored by the RLS policy. Deletion tier 1 (closed).
/// </summary>
public sealed class Share : IOrgScoped
{
    public required Guid Id { get; init; }

    /// <summary>The owning org (IOrgScoped; column owner_org_id). Members read through share_access.</summary>
    public required OrgId OrgId { get; init; }
    public required string OwnerName { get; init; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public ShareStatus Status { get; set; } = ShareStatus.Active;
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
