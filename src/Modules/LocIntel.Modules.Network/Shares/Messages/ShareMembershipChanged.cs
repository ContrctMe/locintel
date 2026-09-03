using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Shares.Messages;

/// <summary>A member changed its own standing (accepted, left); sent to the OWNER, who updates the roster and republishes it.</summary>
public sealed record ShareMembershipChanged(
    Guid ShareId,
    OrgId MemberOrgId,
    MembershipStatus Status,
    DateTimeOffset? JoinedAt
);
