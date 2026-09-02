namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseNoteView(
    Guid Id,
    Guid AuthorId,
    string? Author,
    string Body,
    DateTimeOffset CreatedAt
);
