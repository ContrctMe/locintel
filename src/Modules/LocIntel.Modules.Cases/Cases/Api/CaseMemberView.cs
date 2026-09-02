namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseMemberView(
    Guid Id,
    Guid UserId,
    string? User,
    CaseMemberRole Role,
    DateTimeOffset AddedAt
);
