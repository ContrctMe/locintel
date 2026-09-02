using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Shares;

/// <summary>The roster every member sees: which orgs are in, invited, or gone. Org names are snapshots. Tier 3.</summary>
public sealed class ShareMember
{
    public required Guid Id { get; init; }
    public required Guid ShareId { get; init; }
    public required OrgId OrgId { get; init; }
    public required string OrgName { get; init; }
    public required MemberRole Role { get; init; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Invited;
    public DateTimeOffset? JoinedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
