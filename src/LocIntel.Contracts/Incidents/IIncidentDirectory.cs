namespace LocIntel.Contracts.Incidents;

/// <summary>
/// Incident lookup for modules that attach to incidents (cases, entities,
/// marketplace requests) - implemented by Incidents, consumed above it.
/// Soft-deleted incidents are absent.
/// </summary>
public interface IIncidentDirectory
{
    Task<IncidentInfo?> FindAsync(Guid incidentId, CancellationToken ct = default);
}
