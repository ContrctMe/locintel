using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Incidents.Api;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// The dashboard rollup. Groups on the STAMPED business date and the
/// scope-filtered path (ADR 2/26): "last 30 days per store" is a group-by,
/// never a per-row zone conversion, and a re-parented site's history stays
/// where it happened.
/// </summary>
public static class IncidentStatsEndpoint
{
    [Transactional(typeof(IncidentsDbContext))]
    [WolverineGet("/api/incidents/stats")]
    [ProducesResponseType(typeof(IncidentStatsResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Stats(
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        DateOnly? from,
        DateOnly? to,
        Guid? siteId,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.IncidentsRead, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var end = to ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var start = from ?? end.AddDays(-29);
        if (start > end)
            return Results.BadRequest(new { error = "from must not be after to" });
        if (end.DayNumber - start.DayNumber > 366)
            return Results.BadRequest(new { error = "range is limited to one year" });

        var query = db
            .Incidents.InScope(scope)
            .Where(i => i.BusinessDate >= start && i.BusinessDate <= end);
        if (siteId is { } site)
            query = query.Where(i => i.SiteId == site);

        var rows = await query
            .Select(i => new
            {
                i.Category,
                i.Severity,
                i.SiteId,
                i.BusinessDate,
                i.Status,
                Loss = i.LossAmount ?? 0m,
            })
            .ToListAsync(ct);

        static IReadOnlyList<IncidentCount> Group<TKey>(
            IEnumerable<(TKey Key, decimal Loss)> items,
            Func<TKey, string> label
        )
            where TKey : notnull =>
            items
                .GroupBy(x => x.Key)
                .Select(g => new IncidentCount(label(g.Key), g.Count(), g.Sum(x => x.Loss)))
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.Key)
                .ToList();

        return Results.Ok(
            new IncidentStatsResponse(
                start,
                end,
                rows.Count,
                rows.Count(r => r.Status == IncidentStatus.Open),
                rows.Sum(r => r.Loss),
                Group(rows.Select(r => (r.Category, r.Loss)), k => k.ToString()),
                Group(rows.Select(r => (r.Severity, r.Loss)), k => k.ToString()),
                Group(rows.Select(r => (r.SiteId, r.Loss)), k => k.ToString()),
                rows.GroupBy(r => r.BusinessDate)
                    .Select(g => new IncidentCount(
                        g.Key.ToString("yyyy-MM-dd"),
                        g.Count(),
                        g.Sum(x => x.Loss)
                    ))
                    .OrderBy(c => c.Key)
                    .ToList()
            )
        );
    }
}
