using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Scheduling;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// Rebuild request for one site's occurrence projection (ADR 28). Triggers:
/// schedule create/update/delete, site timezone change (the one everyone
/// forgets), and the horizon roll. Carries no org - tenant rides the message
/// ENVELOPE (ADR 24) so RLS applies in the handler's scope.
/// </summary>
public sealed record RebuildSiteOccurrences(Guid SiteId);

public static class RebuildSiteOccurrencesHandler
{
    public static TimeSpan Horizon => TimeSpan.FromDays(365);

    [Transactional]
    public static async Task Handle(
        RebuildSiteOccurrences message,
        Envelope envelope,
        ITenantContext tenant,
        TenancyDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        // ADR 24: tenant rides the envelope and is read LAZILY by the tenant
        // context (transactional frames open the connection before this body
        // runs). Guard loudly - a missing tenant would otherwise fail closed
        // into a silent no-op rebuild.
        if (tenant.OrgId is null)
            throw new InvalidOperationException(
                $"RebuildSiteOccurrences arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var siteId = new SiteId(message.SiteId);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null)
            return; // deleted since enqueue - projection rows cascade below anyway

        var schedules = await db.SiteSchedules.Where(s => s.SiteId == siteId).ToListAsync(ct);

        var now = time.GetUtcNow();
        var horizonStart = now.AddDays(-1);
        var horizonEnd = now.Add(Horizon);

        await db.SiteOpenWindows.Where(w => w.SiteId == siteId).ExecuteDeleteAsync(ct);
        foreach (var schedule in schedules)
        {
            var occurrences = RecurrenceExpander.Expand(
                schedule.RRule,
                schedule.AnchorDate,
                schedule.OpensLocal,
                schedule.ClosesLocal,
                site.TimeZone,
                schedule.ExDates,
                horizonStart,
                horizonEnd
            );
            db.SiteOpenWindows.AddRange(
                occurrences.Select(o => new SiteOpenWindow
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = site.OrgId,
                    SiteId = site.Id,
                    ScheduleId = schedule.Id,
                    StartsAtUtc = o.StartUtc,
                    EndsAtUtc = o.EndUtc,
                    LocalDate = o.LocalDate,
                })
            );
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Per-org horizon roll (ADR 24 fan-out): the enumerator publishes one of
/// these per org with the tenant on the envelope; this handler sees only its
/// own org's sites (RLS + tenant filter) and fans out per-site rebuilds.
/// </summary>
public sealed record RollOccurrenceHorizons;

public static class RollOccurrenceHorizonsHandler
{
    public static async Task Handle(
        RollOccurrenceHorizons _,
        Envelope envelope,
        ITenantContext tenant,
        TenancyDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"RollOccurrenceHorizons arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var siteIds = await db.Sites.Select(s => s.Id).ToListAsync(ct);
        foreach (var siteId in siteIds)
            await bus.PublishForOrgAsync(org, new RebuildSiteOccurrences(siteId.Value));
    }
}
