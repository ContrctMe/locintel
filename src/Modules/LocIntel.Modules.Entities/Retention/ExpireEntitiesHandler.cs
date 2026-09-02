using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Entities.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace LocIntel.Modules.Entities.Retention;

/// <summary>
/// Trashes (tier 2) every entity past its ExpiresAt that is not under legal
/// hold. Sees only its own org's rows: RLS + the tenant filter, from the
/// envelope's tenant. Each expiry is a domain event with a system actor.
/// </summary>
public static class ExpireEntitiesHandler
{
    public static async Task Handle(
        ExpireEntities _,
        Envelope envelope,
        ITenantContext tenant,
        EntitiesDbContext db,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"ExpireEntities arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var now = time.GetUtcNow();
        var expired = await db
            .Entities.Where(e => e.ExpiresAt <= now && !e.LegalHold)
            .ToListAsync(ct);
        if (expired.Count == 0)
            return;
        foreach (var entity in expired)
        {
            entity.DeletedAt = now;
            entity.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.System,
            "entity.expired",
            new { Ids = expired.Select(e => e.Id), Count = expired.Count }
        );
    }
}
