using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Sites;

public sealed class SiteDirectory(TenancyDbContext db) : ISiteDirectory
{
    public async Task<SiteInfo?> FindAsync(Guid siteId, CancellationToken ct = default)
    {
        var id = new SiteId(siteId);
        return await db
            .Sites.Where(s => s.Id == id)
            .Select(s => new SiteInfo(s.Id.Value, s.Name, s.Path.ToString(), s.TimeZone))
            .FirstOrDefaultAsync(ct);
    }
}
