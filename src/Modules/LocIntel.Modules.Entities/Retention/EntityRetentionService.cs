using LocIntel.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace LocIntel.Modules.Entities.Retention;

/// <summary>
/// The daily enumerator (ADR 24): platform-level, touches no tenant data -
/// reads the org list through the Tenancy contract and enqueues one
/// tenant-scoped sweep per org. Runs in the worker role only.
/// </summary>
public sealed class EntityRetentionService(IServiceProvider services) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await SweepAllAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { } // shutdown
    }

    private async Task SweepAllAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationLookup>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        foreach (var org in await orgs.ListIdsAsync(ct))
            await bus.PublishAsync(
                new ExpireEntities(),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
    }
}
