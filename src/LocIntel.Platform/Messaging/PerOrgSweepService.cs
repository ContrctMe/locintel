using LocIntel.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace LocIntel.Platform.Messaging;

/// <summary>
/// The recurring "do this for every org" background job: wake on an interval,
/// enumerate orgs, publish one envelope-tenanted message each, and let the
/// module's handler do the work under that org's RLS session.
///
/// It exists because the template had three byte-for-byte copies of this loop
/// (audit retention, file trash, occurrence horizons) and a fork wrote two
/// more. Copies drift in the details that matter: swallowing cancellation at
/// shutdown, opening a scope per tick rather than per process, and never
/// letting one org's failure kill the sweep for the rest.
///
/// Subclasses supply the interval and the message; everything else is here -
/// including the per-period lease (<see cref="ISweepLease"/>): with several
/// worker replicas each ticking its own timer, only the first to claim a
/// period publishes, so a fleet produces one logical sweep per period.
/// </summary>
/// <typeparam name="TMessage">
/// A parameterless message - the org travels on the envelope, never in the body.
/// </typeparam>
public abstract class PerOrgSweepService<TMessage>(IServiceProvider services) : BackgroundService
    where TMessage : notnull, new()
{
    /// <summary>How often the sweep runs. Daily is the usual answer.</summary>
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
                var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationEnumerator>();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                foreach (var org in await orgs.ListIdsAsync(stoppingToken))
                    await bus.PublishForOrgAsync(org, new TMessage());
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { } // shutdown, not a fault
    }
}

/// <summary>
/// The org list a sweep walks. Platform cannot reference the module that owns
/// organizations, so the sweep depends on this narrow port and the module's
/// own lookup satisfies it.
/// </summary>
public interface IOrganizationEnumerator
{
    Task<IReadOnlyList<OrgId>> ListIdsAsync(CancellationToken ct = default);
}
