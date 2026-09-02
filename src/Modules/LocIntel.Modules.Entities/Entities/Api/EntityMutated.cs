namespace LocIntel.Modules.Entities.Entities.Api;

public sealed record EntityMutated(
    Guid Id,
    EntityStatus Status,
    DateTimeOffset ExpiresAt,
    bool LegalHold,
    DateTimeOffset? DeletedAt,
    DateTimeOffset UpdatedAt
);
