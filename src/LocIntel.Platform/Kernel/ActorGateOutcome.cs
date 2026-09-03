namespace LocIntel.Platform.Kernel;

/// <summary>
/// <see cref="GateOutcome"/> narrowed to a writer: Allowed when
/// <see cref="Actor"/> is set, otherwise <see cref="Gate"/> is the failure
/// the contract specifies. An Allowed principal with no actor (a contact
/// holding the capability) becomes NotSignedIn - contacts have their own
/// surfaces and never write org data through an actor-stamped endpoint.
/// Pure, so the narrowing is unit-tested; the adapter is ActorGate.
/// </summary>
public sealed record ActorGateOutcome(GateOutcome Gate, ActorRef? Actor, NodeScope? Scope)
{
    public static ActorGateOutcome Of(GateOutcome gate)
    {
        if (gate is GateOutcome.Allowed allowed)
            return ActorRef.From(allowed.Principal) is { } actor
                ? new ActorGateOutcome(gate, actor, allowed.Scope)
                : new ActorGateOutcome(new GateOutcome.NotSignedIn(), null, null);
        return new ActorGateOutcome(gate, null, null);
    }
}
