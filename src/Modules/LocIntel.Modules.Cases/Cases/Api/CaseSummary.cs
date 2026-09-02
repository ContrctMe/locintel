namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseSummary(
    Guid Id,
    string Title,
    CaseStatus Status,
    CasePriority Priority,
    Guid? LeadId,
    string? Lead,
    int IncidentCount,
    int OpenTasks,
    bool LegalHold,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt
);
