using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Identity.Users;

/// <summary>Keeps org_directory current from Tenancy's integration events.</summary>
public static class OrganizationUpsertedHandler
{
    public static async Task Handle(
        OrganizationUpserted evt,
        IdentityDbContext db,
        CancellationToken ct
    )
    {
        // two quick upserts for one org are handled in parallel by the local
        // queue; serialize them so the second sees the first's row (AggregateLock)
        await db.TakeAsync(evt.OrgId.Value, ct);
        var entry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == evt.OrgId, ct);
        if (entry is null)
        {
            db.OrgDirectory.Add(
                new OrgDirectoryEntry
                {
                    OrgId = evt.OrgId,
                    Name = evt.Name,
                    Slug = evt.Slug,
                    Region = evt.Region,
                    ExternalId = evt.ExternalId,
                    Status = evt.Status,
                    IsPlatform = evt.IsPlatform,
                }
            );
        }
        else
        {
            entry.Name = evt.Name;
            entry.Slug = evt.Slug;
            entry.Region = evt.Region;
            entry.ExternalId = evt.ExternalId;
            entry.Status = evt.Status;
            entry.IsPlatform = evt.IsPlatform;
            entry.SyncedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
