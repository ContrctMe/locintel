using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Storage;

public sealed class ReportFileSource(StorageDbContext db) : IReportFileSource
{
    public async Task<IReadOnlyList<IReportFileSource.File>> ReadAsync(
        OrgId org,
        Guid[] ids,
        CancellationToken ct = default
    )
    {
        if (ids.Length > 1000)
            throw new ArgumentOutOfRangeException(nameof(ids));
        return await db
            .Files.AsNoTracking()
            .Where(x =>
                x.OrgId == org
                && ids.Contains(x.Id)
                && x.Status == FileStatus.Clean
                && x.Origin == null
            )
            .Select(x => new IReportFileSource.File(x.Id, x.Key, x.Name, x.ContentType, x.SiteIds))
            .ToListAsync(ct);
    }
}
