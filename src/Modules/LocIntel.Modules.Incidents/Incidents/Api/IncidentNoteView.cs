namespace LocIntel.Modules.Incidents.Incidents.Api;

public sealed record IncidentNoteView(
    Guid Id,
    Guid AuthorId,
    string? Author,
    string Body,
    DateTimeOffset CreatedAt
);
