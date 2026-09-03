using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;

namespace LocIntel.Contracts;

/// <summary>
/// Gates 2 and 3 for endpoints whose writer may be a person OR an API key
/// (ADR 40): <see cref="Gate.RequireAsync"/> plus the <see cref="ActorRef"/>
/// the audit trail and ownership stamps need. A contact principal that
/// somehow holds the capability is treated as not signed in - contacts have
/// their own surfaces and never write org data here.
/// </summary>
public static class ActorGate
{
    public static async ValueTask<ActorGateOutcome> RequireAsync(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string capability,
        CancellationToken ct = default
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, capability, ct);
        if (gate is GateOutcome.Allowed allowed && ActorRef.From(allowed.Principal) is { } actor)
            return new ActorGateOutcome(gate, actor, allowed.Scope);
        return new ActorGateOutcome(
            gate is GateOutcome.Allowed ? new GateOutcome.NotSignedIn() : gate,
            null,
            null
        );
    }
}

/// <summary>Allowed when <see cref="Actor"/> is set; otherwise <see cref="ToResult"/> is the status the contract specifies.</summary>
public sealed record ActorGateOutcome(GateOutcome Gate, ActorRef? Actor, NodeScope? Scope)
{
    public IResult ToResult() => Gate.ToResult();
}
