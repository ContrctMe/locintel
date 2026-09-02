namespace LocIntel.Modules.Incidents.Incidents.Api;

/// <summary>A suggestion, not a change: the person applies it through the normal update.</summary>
public sealed record IncidentAssistResponse(
    string Provider,
    IncidentCategory Category,
    IncidentSeverity Severity,
    string[] Tags,
    string Summary,
    double Confidence
);
