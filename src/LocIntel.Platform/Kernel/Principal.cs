namespace LocIntel.Platform.Kernel;

/// <summary>
/// The three principal tiers (ADR 7). There is no anonymous: an
/// unauthenticated request is a Guest whose org (and later site) derives from
/// the request host. Contact is the middle tier - known via a signed token
/// (magic link, invite) without holding an account.
/// </summary>
public abstract record Principal
{
    public sealed record Guest(OrgId? Org) : Principal;

    public sealed record Contact(Guid ContactId, OrgId Org, string? Email = null) : Principal;

    /// <summary>An API key acting server-to-server: a service principal OF an org (ADR 40).</summary>
    public sealed record Service(Guid KeyId, OrgId Org) : Principal;

    /// <summary>
    /// ImpersonationExpiresAt is set (ADR 42) only while an operator's
    /// support session into ActiveOrg is unexpired; accessors derive it from
    /// claims plus the clock, never the database.
    /// </summary>
    public sealed record User(
        Guid UserId,
        string Email,
        string? Name,
        OrgId? ActiveOrg,
        DateTimeOffset? ImpersonationExpiresAt = null
    ) : Principal
    {
        public bool Impersonating => ImpersonationExpiresAt is not null;
    }
}

/// <summary>
/// Read-time principal resolution (the step-1 lesson: Wolverine's transactional
/// middleware opens connections before request middleware runs, so anything the
/// RLS interceptor needs must be answerable on demand, from any DI scope).
/// Implementations must not hit the database - they read the authenticated
/// claims or state already materialized on the request.
/// </summary>
public interface IPrincipalAccessor
{
    Principal Current { get; }
}
