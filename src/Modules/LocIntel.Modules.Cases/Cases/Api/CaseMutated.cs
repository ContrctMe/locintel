namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseMutated(
    Guid Id,
    CaseStatus Status,
    CasePriority Priority,
    bool LegalHold,
    DateTimeOffset? DeletedAt,
    DateTimeOffset UpdatedAt
);
