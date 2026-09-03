using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Network.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Network;

/// <summary>Network's slice of the offboarding export: the org's standing in each share, what IT published, and the copies it received (ADR 48: all its own rows).</summary>
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
            .Where(b => b.OrgId == org)
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
        var received = await db
            .BulletinCopies.IgnoreQueryFilters()
            .Where(c => c.OrgId == org)
            .Select(c => new
            {
                c.BulletinId,
                c.ShareId,
                c.PublisherName,
                kind = c.Kind.ToString(),
                c.Title,
                c.PublishedAt,
                c.WithdrawnAt,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new
            {
                shares = access,
                published,
                received,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
