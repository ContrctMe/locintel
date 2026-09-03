using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;

namespace LocIntel.Platform.UnitTests;

/// <summary>
/// The narrowing from a gate outcome to a writer (ActorGateOutcome.Of), as
/// pure logic: who becomes an actor, who does not, and that failures pass
/// through untouched so ToResult answers exactly as Gate would.
/// </summary>
public class ActorGateTests
{
    private static readonly OrgId Org = OrgId.New();
    private static readonly NodeScope Scope = new NodeScope.EntireOrg(Org);

    private static GateOutcome Allowed(Principal principal) =>
        new GateOutcome.Allowed(principal, Org, Scope);

    [Fact]
    public void A_user_is_a_user_actor_with_the_scope_passed_through()
    {
        var userId = Guid.NewGuid();
        var outcome = ActorGateOutcome.Of(
            Allowed(new Principal.User(userId, "a@x.test", null, Org))
        );

        Assert.Equal(new ActorRef(Org, userId, "user"), outcome.Actor);
        Assert.False(outcome.Actor!.Value.IsService);
        Assert.Same(Scope, outcome.Scope);
        Assert.Equal(AuditActor.User(userId), outcome.Actor.Value.Audit);
    }

    [Fact]
    public void An_api_key_is_a_service_actor_of_its_org()
    {
        // ADR 40: a service principal is first-class on org data, attributed
        // under its own tier so the audit trail never mistakes it for a person
        var keyId = Guid.NewGuid();
        var outcome = ActorGateOutcome.Of(Allowed(new Principal.Service(keyId, Org)));

        Assert.Equal(new ActorRef(Org, keyId, "service"), outcome.Actor);
        Assert.True(outcome.Actor!.Value.IsService);
        Assert.Equal(AuditActor.Service(keyId), outcome.Actor.Value.Audit);
    }

    [Fact]
    public void A_contact_holding_the_capability_is_not_signed_in_here()
    {
        // contacts have their own surfaces; an actor-stamped write must never
        // carry a contact id, so the outcome is 401 and no actor
        var outcome = ActorGateOutcome.Of(Allowed(new Principal.Contact(Guid.NewGuid(), Org)));

        Assert.Null(outcome.Actor);
        Assert.Null(outcome.Scope);
        Assert.IsType<GateOutcome.NotSignedIn>(outcome.Gate);
    }

    [Fact]
    public void Failures_pass_through_unchanged()
    {
        var forbidden = new GateOutcome.Forbidden("sites:manage");
        var notSignedIn = new GateOutcome.NotSignedIn();

        Assert.Same(forbidden, ActorGateOutcome.Of(forbidden).Gate);
        Assert.Same(notSignedIn, ActorGateOutcome.Of(notSignedIn).Gate);
        Assert.Null(ActorGateOutcome.Of(forbidden).Actor);
    }

    [Fact]
    public void Guests_and_orgless_users_never_resolve_to_an_actor()
    {
        Assert.Null(ActorRef.From(new Principal.Guest(Org)));
        Assert.Null(
            ActorRef.From(new Principal.User(Guid.NewGuid(), "a@x.test", null, ActiveOrg: null))
        );
    }
}
