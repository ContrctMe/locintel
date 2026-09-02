using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Identity.Access;

/// <summary>
/// A role is a BUNDLE humans manage (ADR 6); the evaluator only ever sees the
/// compiled grants. Org-defined, so each org shapes its own vocabulary.
/// </summary>
public sealed class Role : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Name { get; set; }

    public static Role Create(OrgId orgId, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = orgId,
            Name = name,
        };
}

/// <summary>(domain, action) grant carried by a role. "*" wildcards either part.</summary>
public sealed class RoleGrant : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid RoleId { get; init; }
    public required string Domain { get; init; }
    public required string Action { get; init; }
}

/// <summary>
/// A role held by a membership AT a scope: null path = the whole org, a node
/// path = that subtree. Assigning at a scope is how "manages 4 of 60 sites"
/// stays out of the session token (the design's core argument).
/// </summary>
public sealed class MembershipRole : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid MembershipId { get; init; }
    public required Guid RoleId { get; init; }
    public string? ScopePath { get; init; }
}

/// <summary>
/// Additive, time-boxed individual grant (ADR 6): reason + grantor + expiry,
/// first-class and auditable. There is NO deny - evaluation stays monotonic.
/// </summary>
public sealed class GrantException : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid UserId { get; init; }
    public required string Domain { get; init; }
    public required string Action { get; init; }
    public string? ScopePath { get; init; }
    public required string Reason { get; init; }
    public required Guid GrantedBy { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
