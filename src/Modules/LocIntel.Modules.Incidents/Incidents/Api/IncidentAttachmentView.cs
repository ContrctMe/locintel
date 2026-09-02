namespace LocIntel.Modules.Incidents.Incidents.Api;

public sealed record IncidentAttachmentView(
    Guid Id,
    Guid FileId,
    string? FileName,
    string? ContentType,
    string? FileStatus,
    string? Label,
    DateTimeOffset AddedAt
);
