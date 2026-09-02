namespace LocIntel.Modules.Incidents.Imports.Api;

public sealed record ImportBatchView(
    Guid Id,
    Guid FileId,
    string FileName,
    ImportStatus Status,
    int Total,
    int Valid,
    int Invalid,
    Guid CreatedBy,
    string? Creator,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CommittedAt
);
