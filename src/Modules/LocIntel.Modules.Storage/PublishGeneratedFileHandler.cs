using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Storage;

public static class PublishGeneratedFileHandler
{
    [Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        PublishGeneratedFile message,
        StorageDbContext db,
        ITenantContext tenant,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("File publication requires an organization.");
        await db.TakeAsync(org.Value, ct);
        if (await db.PurgedOrganizations.AnyAsync(x => x.OrgId == org, ct))
            return;
        var existing = await db.Files.SingleOrDefaultAsync(
            x => x.OrgId == org && x.Id == message.Id,
            ct
        );
        if (existing is null)
        {
            if (
                message.Bytes <= 0
                || message.SiteIds.Length is < 1 or > 1000
                || !message.Key.StartsWith(
                    $"{tenant.Region.Value}/{org.Value}/",
                    StringComparison.Ordinal
                )
            )
                throw new InvalidOperationException("Invalid generated file publication.");
            db.Files.Add(
                new FileObject
                {
                    Id = message.Id,
                    OrgId = org,
                    Key = message.Key,
                    Name = message.Name,
                    ContentType = message.ContentType,
                    MaxBytes = message.Bytes,
                    CreatedBy = message.CreatedBy,
                    CreatedAt = message.CreatedAt,
                    SiteIds = message.SiteIds.Distinct().ToArray(),
                    Origin = message.Origin,
                    OriginId = message.OriginId,
                    Status = FileStatus.Clean,
                    ScannedAt = message.CreatedAt,
                }
            );
        }
        else if (
            existing.Key != message.Key
            || existing.Origin != message.Origin
            || existing.OriginId != message.OriginId
        )
            throw new InvalidOperationException("Generated file identity conflict.");
        // Never reset an existing file's trash/erasure/hold state on redelivery.
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(org, new GeneratedFilePublished(message.Id, message.Origin));
    }
}
