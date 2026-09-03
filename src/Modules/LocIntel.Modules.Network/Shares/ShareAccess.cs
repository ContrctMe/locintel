using System.Text.Json;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Shares;

/// <summary>
/// An org's OWN standing in a share, and its projection of the share (ADR
/// 48): name, description, status and the roster arrive from the owner
/// through the outbox. Created under the invitee's tenant by the invitation
/// handler, never by the inviter. The owner holds one too, for a uniform
/// list.
/// </summary>
public sealed class ShareAccess : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid ShareId { get; init; }
    public required OrgId OwnerOrgId { get; init; }
    public required string ShareName { get; set; }
    public string Description { get; set; } = "";
    public required string OwnerName { get; set; }
    public ShareStatus ShareStatus { get; set; } = ShareStatus.Active;
    public required MemberRole Role { get; init; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Invited;
    public Guid? InvitedBy { get; init; }
    public DateTimeOffset? JoinedAt { get; set; }

    /// <summary>RosterEntry[] as jsonb, as the owner last published it.</summary>
    public string RosterJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public List<RosterEntry> Roster() =>
        JsonSerializer.Deserialize<List<RosterEntry>>(RosterJson) ?? [];
}
