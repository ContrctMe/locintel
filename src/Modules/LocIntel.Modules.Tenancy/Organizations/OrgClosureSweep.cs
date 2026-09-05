using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace LocIntel.Modules.Tenancy.Organizations;

/// <summary>Per-org check: a closure whose grace window has passed offboards now.</summary>
public sealed record ProcessOrgClosure;

public static class ProcessOrgClosureHandler
{
    [Wolverine.Attributes.Transactional(typeof(TenancyDbContext))]
    public static async Task Handle(
        ProcessOrgClosure _,
        Envelope envelope,
        ITenantContext tenant,
        TenancyDbContext db,
        IConfiguration configuration,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } orgId)
            throw new InvalidOperationException(
                $"ProcessOrgClosure arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (
            org is null
            || org.CloseRequestedAt is not { } requestedAt
            || org.Status == OrganizationStatus.Offboarding
            || requestedAt.AddDays(OrgClosureEndpoints.GraceDays(configuration))
                > DateTimeOffset.UtcNow
        )
            return;

        // the window closed with no cancel: this IS the offboard (the
        // operator two-step exists for custody decisions; the tenant made
        // theirs, thirty days ago, with every manager notified)
        org.Status = OrganizationStatus.Offboarding;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new OrganizationUpserted(
                org.Id,
                org.Name,
                org.Slug,
                org.Region,
                org.ExternalId,
                org.Version,
                org.Status.ToString(),
                org.IsPlatform
            )
        );
        await bus.AuditAsync(
            org.Id,
            AuditActor.System,
            "org.offboarded",
            new { source = "self-serve-closure" }
        );
        await OrgPurgeFanOut.PublishAsync(bus, orgId, org.ExternalId);
    }
}

/// <summary>Daily enumerator (same shape as the audit retention sweep).</summary>
public sealed class OrgClosureService(IServiceProvider services)
    : PerOrgSweepService<ProcessOrgClosure>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
}
