namespace LocIntel.Contracts.Incidents;

/// <summary>
/// Incident lookup for modules that attach to incidents (cases, entities,
/// marketplace requests) - implemented by Incidents, consumed above it.
/// Soft-deleted incidents are absent.
/// </summary>
public interface IIncidentDirectory
{
    Task<IncidentInfo?> FindAsync(Guid incidentId, CancellationToken ct = default);

    /// <summary>A site's incidents on one stamped business date (daily activity reports). Tenant-filtered, not scope-filtered: callers apply their own gate 3.</summary>
    Task<IReadOnlyList<IncidentInfo>> ListForDayAsync(
        Guid siteId,
        DateOnly businessDate,
        CancellationToken ct = default
    );
}
