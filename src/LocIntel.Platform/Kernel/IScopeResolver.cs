namespace LocIntel.Platform.Kernel;

/// <summary>
/// The authorization port's two operations (the design's core claim): the
/// point check AND the set-valued scope for list endpoints. Step 3 ships the
/// scope mechanism with membership-level semantics (a member's grant covers
/// the whole org); step 4's roles/grants/exceptions replace the default
/// implementation without touching any call site.
/// </summary>
public interface IScopeResolver
{
    ValueTask<bool> CanAsync(Principal principal, string action, CancellationToken ct = default);
    ValueTask<NodeScope> ScopeForAsync(
        Principal principal,
        string action,
        CancellationToken ct = default
    );
}

/// <summary>
/// Step-3 semantics: an authenticated user acting in their active org holds
/// every action over the entire org; guests and contacts hold none of the
/// management actions. Monotonic and deliberately coarse (ADR 6) - roles
/// compile to grants in step 4.
/// </summary>
public sealed class MembershipScopeResolver : IScopeResolver
{
    public ValueTask<bool> CanAsync(
        Principal principal,
        string action,
        CancellationToken ct = default
    ) => ValueTask.FromResult(principal is Principal.User { ActiveOrg: not null });

    public ValueTask<NodeScope> ScopeForAsync(
        Principal principal,
        string action,
        CancellationToken ct = default
    ) =>
        ValueTask.FromResult<NodeScope>(
            principal switch
            {
                Principal.User { ActiveOrg: { } org } => new NodeScope.EntireOrg(org),
                _ => NodeScope.Nothing,
            }
        );
}

/// <summary>
/// The operator boundary (gate 2's platform edition): true only when the
/// principal's ACTIVE org is the flagged platform org AND they hold
/// platform:operate there. An ordinary org Owner's *:* wildcard never crosses
/// this line - the org flag is the wall, the capability refines within it.
/// </summary>
public interface IOperatorContext
{
    ValueTask<bool> IsOperatorAsync(Principal principal, CancellationToken ct = default);
}
