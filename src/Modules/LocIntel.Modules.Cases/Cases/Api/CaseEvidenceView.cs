namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseEvidenceView(
    Guid Id,
    Guid FileId,
    string? FileName,
    string? ContentType,
    string? FileStatus,
    string? Label,
    Guid AddedBy,
    string? AddedByLabel,
    DateTimeOffset AddedAt
);
