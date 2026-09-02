namespace LocIntel.Modules.Cases.Cases.Api;

/// <summary>The opener becomes Lead. Incidents must be within the opener's cases:manage scope.</summary>
public sealed record OpenCaseRequest(
    string Title,
    string? Summary = null,
    CasePriority Priority = CasePriority.Medium,
    Guid[]? IncidentIds = null
);
