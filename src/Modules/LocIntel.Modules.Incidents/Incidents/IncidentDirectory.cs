using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Incidents.Data;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Incidents.Incidents;

public sealed class IncidentDirectory(IncidentsDbContext db) : IIncidentDirectory
{
    public async Task<IncidentInfo?> FindAsync(Guid incidentId, CancellationToken ct = default) =>
        await db
            .Incidents.Where(i => i.Id == incidentId)
            .Select(i => new IncidentInfo(
                i.Id,
                i.SiteId,
                i.Path.ToString(),
                i.Title,
                i.Category.ToString(),
                i.Severity.ToString(),
                i.Status.ToString(),
                i.OccurredAt
            ))
            .FirstOrDefaultAsync(ct);
}
