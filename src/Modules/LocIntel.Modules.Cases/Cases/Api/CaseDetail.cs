namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseDetail(
    Guid Id,
    string Title,
    string Summary,
    CaseStatus Status,
    CasePriority Priority,
    Guid? LeadId,
    string? Lead,
    CaseDisposition? Disposition,
    DateTimeOffset? ClosedAt,
    string? ClosureNote,
    bool LegalHold,
    bool CanManage,
    bool CanWork,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<CaseIncidentView> Incidents,
    IReadOnlyList<CaseEntityView> Entities,
    IReadOnlyList<CaseMemberView> Members,
    IReadOnlyList<CaseTaskView> Tasks,
    IReadOnlyList<CaseNoteView> Notes,
    IReadOnlyList<CaseEvidenceView> Evidence,
    int CustodyEvents
);
