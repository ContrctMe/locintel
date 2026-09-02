using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LocIntel.Modules.Tenancy.Data;
using Wolverine;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// The horizon-roll enumerator (ADR 24/28): daily, cross-org platform work
/// that itself touches no tenant data - it reads the platform-global org list
/// and enqueues one tenant-scoped message per org. Runs in the worker role.
/// </summary>
public sealed class HorizonRollService(IServiceProvider services) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await RollAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { } // shutdown
    }

    private async Task RollAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        // organizations are platform-global: readable with no tenant set
        var orgIds = await db.Organizations.Select(o => o.Id).ToListAsync(ct);
        foreach (var orgId in orgIds)
            await bus.PublishForOrgAsync(orgId, new RollOccurrenceHorizons());
    }
}
