using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Network.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Network;

/// <summary>Network's slice of the offboarding export: the org's standing in each share and what IT published. Other orgs' bulletins are theirs.</summary>
public sealed class NetworkExporter(NetworkDbContext db) : IOrgDataExporter
{
    public string Section => "network";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var access = await db
            .Access.IgnoreQueryFilters()
            .Where(a => a.OrgId == org)
            .Select(a => new
            {
                a.ShareId,
                a.ShareName,
                role = a.Role.ToString(),
                status = a.Status.ToString(),
                a.JoinedAt,
            })
            .ToListAsync(ct);
        var published = await db
            .Bulletins.IgnoreQueryFilters()
            .Where(b => b.PublisherOrgId == org)
            .Select(b => new
            {
                b.Id,
                b.ShareId,
                kind = b.Kind.ToString(),
                severity = b.Severity.ToString(),
                b.Title,
                b.Body,
                b.DisplayName,
                b.Aliases,
                descriptors = b.DescriptorsJson,
                b.PublishedAt,
                b.ExpiresAt,
                b.WithdrawnAt,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { shares = access, published },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
