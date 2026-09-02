namespace LocIntel.Modules.Network.Shares.Api;

public sealed record MemberView(
    Guid OrgId,
    string OrgName,
    MemberRole Role,
    MembershipStatus Status,
    DateTimeOffset? JoinedAt
);
