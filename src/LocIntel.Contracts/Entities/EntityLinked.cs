namespace LocIntel.Contracts.Entities;

/// <summary>
/// Integration event (ADR 23/37): Entities publishes one per new
/// entity-incident link with the tenant on the envelope. LinkCount is the
/// entity's total after this link, so subscribers (Alerts) can spot a
/// repeat offender without reading the entities schema.
/// </summary>
public sealed record EntityLinked(
    Guid EntityId,
    string Kind,
    string DisplayName,
    Guid IncidentId,
    string IncidentTitle,
    Guid SiteId,
    string Path,
    int LinkCount,
    Guid LinkedBy
);
