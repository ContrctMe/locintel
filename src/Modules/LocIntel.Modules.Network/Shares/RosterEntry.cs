namespace LocIntel.Modules.Network.Shares;

/// <summary>One line of the roster snapshot every member's access row carries.</summary>
public sealed record RosterEntry(
    Guid OrgId,
    string OrgName,
    MemberRole Role,
    MembershipStatus Status,
    DateTimeOffset? JoinedAt
);
