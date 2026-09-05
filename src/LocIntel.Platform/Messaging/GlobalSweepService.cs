using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace LocIntel.Platform.Messaging;

/// <summary>
/// The recurring "do this once for the platform" background job, the
/// sibling of <see cref="PerOrgSweepService{TMessage}"/>: wake on an
/// interval, claim the period's lease, publish ONE message, and let the
/// handler do the work. The message carries no tenant - it is platform
/// upkeep (idempotency cleanup, partition upkeep), never org data.
/// </summary>
public abstract class GlobalSweepService<TMessage>(IServiceProvider services) : BackgroundService
    where TMessage : notnull, new()
{
    protected abstract TimeSpan Interval { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hosted services start in registration order and the modules register
        // before Wolverine: a first tick that publishes before Wolverine's own
        // hosted service has started throws WolverineHasNotStartedException
        // and, under the host's default StopHost behaviour, takes the process
        // down. Seen in the image smoke, never in the test host (timing). Wait
        // for the host to be fully started before the first tick.
        await HostStarted.WaitAsync(services, stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await using var scope = services.CreateAsyncScope();
                var lease = scope.ServiceProvider.GetRequiredService<ISweepLease>();
                if (
                    !await lease.TryClaimAsync(
                        SweepIdentity.For<TMessage>(),
                        Interval,
                        stoppingToken
                    )
                )
                    continue; // another replica owns this period
                await scope
                    .ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new TMessage());
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { } // shutdown, not a fault
    }
}
