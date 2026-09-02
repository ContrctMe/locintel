namespace LocIntel.Modules.Incidents.Incidents.Api;

/// <summary>The state a mutation left the incident in - typed so the generated client stays honest (ADR 16).</summary>
public sealed record IncidentMutated(
    Guid Id,
    IncidentStatus Status,
    bool LegalHold,
    DateOnly BusinessDate,
    DateTimeOffset? DeletedAt,
    DateTimeOffset UpdatedAt
);
