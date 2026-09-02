using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Shares;

/// <summary>
/// An org's OWN standing in a share - the single-owner row every cross-org
/// policy in this schema hangs off (one hop, no cycles). Created under the
/// invitee's tenant by the invitation handler, never by the inviter.
/// </summary>
public sealed class ShareAccess : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid ShareId { get; init; }
    public required string ShareName { get; init; }
    public required MemberRole Role { get; init; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Invited;
    public Guid? InvitedBy { get; init; }
    public DateTimeOffset? JoinedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
