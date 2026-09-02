using LocIntel.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>The SLA enumerator (ADR 24): every five minutes, one tenant-scoped sweep per org. Worker role only.</summary>
public sealed class RequestSlaService(IServiceProvider services) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await using var scope = services.CreateAsyncScope();
                var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationLookup>();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                foreach (var org in await orgs.ListIdsAsync(stoppingToken))
                    await bus.PublishAsync(
                        new EscalateOverdueRequests(),
                        new DeliveryOptions { TenantId = org.Value.ToString() }
                    );
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { } // shutdown
    }
}
