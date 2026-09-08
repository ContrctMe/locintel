using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Reporting;

/// <summary>Lifecycle metadata only; report bytes stay behind current file/resource authorization.</summary>
public sealed class ReportingExporter(ReportingDbContext db) : IOrgDataExporter
{
    public string Section => "reporting";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default) =>
        JsonSerializer.Serialize(
            await db
                .Jobs.IgnoreQueryFilters()
                .Where(x => x.OrgId == org)
                .Select(x => new
                {
                    x.Id,
                    x.ReportType,
                    x.Mode,
                    x.State,
                    x.CreatedAt,
                    x.CompletedAt,
                    x.ExpiresAt,
                })
                .ToBoundedExportListAsync(ct),
            JsonSerializerOptions.Web
        );
}
