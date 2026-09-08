using LocIntel.Contracts;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Reporting;

public static class GeneratedFilePublishedHandler
{
    [Transactional(typeof(ReportingDbContext))]
    public static Task Handle(
        GeneratedFilePublished message,
        ReportingDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    ) =>
        message.Origin == "report"
            ? db
                .Artifacts.Where(x =>
                    x.OrgId == tenant.OrgId && x.Id == message.FileId && x.Ready && x.ItemId != null
                )
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.FilePublished, true), ct)
            : Task.CompletedTask;
}
