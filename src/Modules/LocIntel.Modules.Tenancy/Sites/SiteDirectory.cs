using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Tenancy.Sites;

public sealed class SiteDirectory(TenancyDbContext db) : ISiteDirectory
{
    public async Task<SiteInfo?> FindAsync(Guid siteId, CancellationToken ct = default)
    {
        var id = new SiteId(siteId);
        // the hierarchy id lives on the node, and ADR 2/4 keys the stamped
        // ancestor path by it - so the directory joins rather than making
        // every consumer do it
        return await db
            .Sites.Where(s => s.Id == id)
            .Join(db.HierarchyNodes, s => s.NodeId, n => n.Id, (s, n) => new { Site = s, Node = n })
            .Select(x => new SiteInfo(
                x.Site.Id.Value,
                x.Site.Name,
                x.Site.Path.ToString(),
                x.Site.TimeZone,
                x.Node.HierarchyId,
                x.Site.Latitude,
                x.Site.Longitude,
                x.Site.City,
                x.Site.PostalCode,
                x.Site.CountryCode
            ))
            .FirstOrDefaultAsync(ct);
    }
}
