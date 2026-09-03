using LocIntel.Platform.Kernel;

namespace LocIntel.Contracts;

/// <summary>
/// Gates 2 and 3 for endpoints whose writer may be a person OR an API key
/// (ADR 40): <see cref="Gate.RequireAsync"/> plus the <see cref="ActorRef"/>
/// the audit trail and ownership stamps need, in one call. The shape:
/// <code>
/// var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.X, ct);
/// if (gate.Actor is not { } actor) return gate.ToResult();
/// </code>
/// </summary>
public static class ActorGate
{
    public static async ValueTask<ActorGateOutcome> RequireAsync(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string capability,
        CancellationToken ct = default
    ) => ActorGateOutcome.Of(await Gate.RequireAsync(accessor, scopes, capability, ct));
}
