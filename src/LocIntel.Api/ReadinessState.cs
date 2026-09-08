using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace LocIntel.Api;

/// <summary>
/// /healthz gates on this: 503 until ready. In Development the DevBootstrap
/// (migrations + seed) flips it, so Aspire's WaitFor(api) - and therefore the
/// console starting - means "the stack is actually usable", closing the
/// cold-start race where an early Sign in click hit a 404.
/// </summary>
public sealed class ReadinessState(
    bool ready,
    IRegionDataSources regions,
    IWolverineRuntime runtime
)
{
    private volatile bool _ready = ready;
    public bool Ready => _ready;

    public void MarkReady() => _ready = true;

    public async Task<bool> DependenciesReadyAsync(string role, CancellationToken ct)
    {
        if (!Ready)
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            runtime.AssertHasStarted();
            if (runtime.Cancellation.IsCancellationRequested)
                return false;
            var localQueues = runtime
                .Endpoints.ActiveSendingAgents()
                .Where(agent => agent.Destination.Scheme == "local")
                .ToArray();
            if (
                !localQueues.Any(agent => agent.IsDurable)
                || localQueues.Any(agent => agent.Latched)
                || localQueues
                    .OfType<IListenerCircuit>()
                    .Any(queue => queue.Status != ListeningStatus.Accepting)
            )
                return false;
            var listeners = runtime.Endpoints.ActiveListeners().ToArray();
            if (
                listeners.Length == 0
                || listeners.Any(listener =>
                    listener.Status != ListeningStatus.Accepting || listener.ReceiverHasFaulted
                )
            )
                return false;

            await using var connection = await regions
                .For(RegionId.Default)
                .OpenConnectionAsync(timeout.Token);
            await using var command = connection.CreateCommand();
            // Both serving roles publish and handle durable local messages. Test
            // each privilege separately: PostgreSQL's comma-list form means ANY,
            // not ALL, and a readable inbox need not be writable or acknowledgeable.
            command.CommandText = """
                SELECT bool_and(has_table_privilege(current_user, relation, privilege)
                    AND has_schema_privilege(current_user, split_part(relation, '.', 1), 'USAGE'))
                FROM unnest(ARRAY[
                    @role_table,
                    'wolverine.wolverine_incoming_envelopes',
                    'wolverine.wolverine_outgoing_envelopes',
                    'wolverine.wolverine_dead_letters'
                ]) AS relation
                CROSS JOIN unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'DELETE']) AS privilege
                """;
            command.Parameters.AddWithValue(
                "role_table",
                role switch
                {
                    "api" => "identity.user_sessions",
                    "worker" => "platform.sweep_runs",
                    _ => throw new InvalidOperationException(
                        "Readiness is defined only for serving roles"
                    ),
                }
            );
            return await command.ExecuteScalarAsync(timeout.Token) is true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
