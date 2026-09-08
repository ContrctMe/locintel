using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Tenancy.Sites;

public sealed class SiteSource(TenancyDbContext db) : ISiteSource
{
    public async Task<IReadOnlyList<ISiteSource.Site>> SelectAsync(
        OrgId org,
        NodeScope scope,
        Guid[]? ids,
        int limit,
        CancellationToken ct = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var query = db.Sites.AsNoTracking().Where(x => x.OrgId == org).InScope(scope);
        if (ids is not null)
        {
            var keys = ids.Select(x => new SiteId(x)).ToArray();
            query = query.Where(x => keys.Contains(x.Id));
        }
        var sites = await query.OrderBy(x => x.Id).Take(checked(limit + 1)).ToListAsync(ct);
        var ancestorIds = sites
            .SelectMany(x => x.Path.ToString().Split('.'))
            .Where(x => x.StartsWith('n'))
            .Select(x => Guid.ParseExact(x[1..], "N"))
            .Distinct()
            .ToArray();
        var ancestors = await db
            .HierarchyNodes.Where(x => x.OrgId == org && ancestorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return sites
            .Select(x => new ISiteSource.Site(
                x.Id.Value,
                x.Name,
                x.Path.ToString(),
                string.Join(
                    " / ",
                    x.Path.ToString()
                        .Split('.')
                        .Where(p => p.StartsWith('n'))
                        .Select(p =>
                            ancestors.GetValueOrDefault(Guid.ParseExact(p[1..], "N"), "Unavailable")
                        )
                ),
                x.Status.ToString(),
                x.TimeZone,
                x.AddressLine1,
                x.City,
                x.PostalCode,
                x.CountryCode,
                x.Latitude,
                x.Longitude,
                x.AttributesJson
            ))
            .ToArray();
    }

    public async Task<IReadOnlyList<ISiteSource.Hours>> HoursAsync(
        OrgId org,
        NodeScope scope,
        Guid[] ids,
        DateOnly from,
        DateOnly through,
        CancellationToken ct = default
    )
    {
        if (through < from || through.DayNumber - from.DayNumber > 31)
            throw new ArgumentOutOfRangeException(nameof(through));
        var keys = ids.Select(x => new SiteId(x)).ToArray();
        var authorized = db
            .Sites.Where(x => x.OrgId == org && keys.Contains(x.Id))
            .InScope(scope)
            .Select(x => x.Id);
        return await db
            .SiteOpenWindows.AsNoTracking()
            .Where(x =>
                x.OrgId == org
                && authorized.Contains(x.SiteId)
                && x.LocalDate >= from
                && x.LocalDate <= through
            )
            .OrderBy(x => x.SiteId)
            .ThenBy(x => x.StartsAtUtc)
            .Select(x => new ISiteSource.Hours(
                x.SiteId.Value,
                x.LocalDate,
                x.StartsAtUtc,
                x.EndsAtUtc
            ))
            .ToListAsync(ct);
    }
}
