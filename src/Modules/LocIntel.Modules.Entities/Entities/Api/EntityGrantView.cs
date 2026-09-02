namespace LocIntel.Modules.Entities.Entities.Api;

public sealed record EntityGrantView(
    Guid Id,
    Guid UserId,
    string? User,
    string Reason,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt
);
