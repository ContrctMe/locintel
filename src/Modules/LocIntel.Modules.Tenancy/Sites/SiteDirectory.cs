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
        return await (
            from s in db.Sites
            join n in db.HierarchyNodes on s.NodeId equals n.Id
            where s.Id == id
            select new SiteInfo(s.Id.Value, s.Name, s.Path.ToString(), s.TimeZone, n.HierarchyId)
        ).FirstOrDefaultAsync(ct);
    }
}
