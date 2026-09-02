namespace LocIntel.Modules.Network.Shares.Api;

public sealed record ShareSummary(
    Guid Id,
    string Name,
    string Description,
    string OwnerName,
    bool Owned,
    MemberRole Role,
    MembershipStatus Membership,
    ShareStatus Status,
    int ActiveMembers,
    int ActiveBulletins,
    DateTimeOffset CreatedAt
);
