namespace LocIntel.Platform.Kernel;

/// <summary>
/// The outcome of the three gates for one request (ADRs 6/7/8): pure data,
/// mapped to HTTP by the adapter in Contracts. Every endpoint used to
/// re-derive this by hand - principal shape, capability check, scope, and
/// the status code for each failure - in at least five textual variants,
/// and the variants disagreed with the documented contract (401 where 403
/// is specified). One outcome type means one mapping.
/// </summary>
public abstract record GateOutcome
{
    /// <summary>The request may proceed; the scope filters what it sees.</summary>
    public sealed record Allowed(Principal Principal, OrgId Org, NodeScope Scope) : GateOutcome;

    /// <summary>
    /// No org-bearing principal: a guest, or a user with no active org. 401 -
    /// the caller must sign in or switch org before authorization can even be
    /// asked. A user without an active org stays 401 on purpose: the console
    /// treats it as "pick an org", not as a permission failure.
    /// </summary>
    public sealed record NotSignedIn : GateOutcome;

    /// <summary>Signed in with an org, but does not hold (domain, action). 403 - gate 2.</summary>
    public sealed record Forbidden(string Capability) : GateOutcome;
}

/// <summary>
/// Gates 2 and 3 as one call. Gate 1 (entitlements) stays at the creation
/// point because it needs the count and increment only the endpoint knows;
/// <see cref="Contracts"/> carries the single 402 body shape for it.
///
/// Two entry points because the principal rule genuinely varies:
/// <c>RequireAsync</c> accepts any principal the resolver can answer for
/// (users, API keys, contacts) - the shape for org-scoped data endpoints;
/// <c>RequireUserAsync</c> insists on a signed-in person - the shape for
/// human-only surfaces (members, roles, account). Service keys on a
/// user-only endpoint are NotSignedIn, which is the existing, documented
/// behaviour.
/// </summary>
public static class Gate
{
    public static async ValueTask<GateOutcome> RequireAsync(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string capability,
        CancellationToken ct = default
    )
    {
        var principal = accessor.Current;
        if (OrgOf(principal) is not { } org)
            return new GateOutcome.NotSignedIn();
        if (!await scopes.CanAsync(principal, capability, ct))
            return new GateOutcome.Forbidden(capability);
        return new GateOutcome.Allowed(
            principal,
            org,
            await scopes.ScopeForAsync(principal, capability, ct)
        );
    }

    public static async ValueTask<GateOutcome> RequireUserAsync(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string capability,
        CancellationToken ct = default
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org } user)
            return new GateOutcome.NotSignedIn();
        if (!await scopes.CanAsync(user, capability, ct))
            return new GateOutcome.Forbidden(capability);
        return new GateOutcome.Allowed(user, org, await scopes.ScopeForAsync(user, capability, ct));
    }

    /// <summary>Platform operators only (platform:operate). A signed-in non-operator is Forbidden.</summary>
    public static async ValueTask<GateOutcome> RequireOperatorAsync(
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        CancellationToken ct = default
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org } user)
            return new GateOutcome.NotSignedIn();
        if (!await operators.IsOperatorAsync(user, ct))
            return new GateOutcome.Forbidden(Capabilities.PlatformOperate);
        return new GateOutcome.Allowed(user, org, new NodeScope.EntireOrg(org));
    }

    private static OrgId? OrgOf(Principal principal) =>
        principal switch
        {
            Principal.User { ActiveOrg: { } org } => org,
            Principal.Service service => service.Org,
            Principal.Contact contact => contact.Org,
            _ => null, // guests are principals, but not org-bearing for authz
        };
}
