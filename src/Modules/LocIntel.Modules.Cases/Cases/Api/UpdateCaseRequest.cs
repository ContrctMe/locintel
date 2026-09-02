namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record UpdateCaseRequest(
    string Title,
    string? Summary,
    CasePriority Priority,
    Guid? LeadId
);
