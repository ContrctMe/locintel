namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record AddCaseTaskRequest(
    string Title,
    Guid? AssigneeId = null,
    DateTimeOffset? DueAt = null
);
