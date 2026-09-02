namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseTaskView(
    Guid Id,
    string Title,
    Guid? AssigneeId,
    string? Assignee,
    DateTimeOffset? DueAt,
    DateTimeOffset? DoneAt,
    DateTimeOffset CreatedAt
);
