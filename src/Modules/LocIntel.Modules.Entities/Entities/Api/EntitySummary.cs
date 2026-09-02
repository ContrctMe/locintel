namespace LocIntel.Modules.Entities.Entities.Api;

public sealed record EntitySummary(
    Guid Id,
    EntityKind Kind,
    EntityStatus Status,
    string DisplayName,
    string[] Aliases,
    int LinkCount,
    DateTimeOffset ExpiresAt,
    bool LegalHold,
    DateTimeOffset? DeletedAt
);
