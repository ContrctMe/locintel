using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
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
                org.Status.ToString(),
                org.IsPlatform
            )
        );
        await bus.PublishAsync(
            new RecordDomainAudit("org.offboarded", """{"source":"self-serve-closure"}"""),
            new DeliveryOptions
            {
                TenantId = org.Id.Value.ToString(),
                Headers = { ["locintel-actor-tier"] = "system" },
            }
        );
        await bus.PublishForOrgAsync(orgId, new PurgeOrgSites());
        await bus.PublishForOrgAsync(orgId, new PurgeOrgFiles());
        await bus.PublishForOrgAsync(orgId, new PurgeOrgEntitlements());
        await bus.PublishForOrgAsync(orgId, new PurgeOrgIngest());
        await bus.PublishForOrgAsync(orgId, new PurgeOrgWebhooks());
        await bus.PublishForOrgAsync(orgId, new OrganizationDeleted(org.Id, org.ExternalId));
    }
}

/// <summary>Daily enumerator (same shape as the audit retention sweep).</summary>
public sealed class OrgClosureService(IServiceProvider services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            do
            {
                await using var scope = services.CreateAsyncScope();
                var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationLookup>();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                foreach (var orgId in await orgs.ListIdsAsync(stoppingToken))
                    await bus.PublishAsync(
                        new ProcessOrgClosure(),
                        new DeliveryOptions { TenantId = orgId.Value.ToString() }
                    );
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { } // shutdown
    }
}
