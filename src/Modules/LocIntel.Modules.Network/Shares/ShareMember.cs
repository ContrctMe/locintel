using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Shares;

/// <summary>The owner's roster - the recipient list as DATA on the owner's side (ADR 48). OrgId is the owner; MemberOrgId the member. Org names are snapshots. Tier 3.</summary>
public sealed class ShareMember : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid ShareId { get; init; }
    public required OrgId MemberOrgId { get; init; }
    public required string OrgName { get; init; }
    public required MemberRole Role { get; init; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Invited;
    public DateTimeOffset? JoinedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public RosterEntry Entry() => new(MemberOrgId.Value, OrgName, Role, Status, JoinedAt);
}
