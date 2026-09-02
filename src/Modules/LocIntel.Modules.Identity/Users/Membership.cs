using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// A user's membership in an org (ADR 5: one user, many orgs). OrgId is a
/// plain reference, no cross-schema FK - modules stay physically decoupled
/// (ADR 17). Deletion tier 3: a membership that ends is hard-deleted; the
/// audit trail (step 5) is the record. Roles/grants attach in step 4.
/// </summary>
public sealed class Membership
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required OrgId OrgId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static Membership Create(Guid userId, OrgId orgId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OrgId = orgId,
        };
}
