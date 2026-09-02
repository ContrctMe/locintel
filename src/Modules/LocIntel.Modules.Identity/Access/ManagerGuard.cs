using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Identity.Access;

/// <summary>
/// The last-manager guard: an org must never lose its final member capable of
/// roles:manage - removing, unassigning, or editing your way past this leaves
/// an org nobody can administer. "Manager" = holds any role whose grants
/// match roles:manage (wildcards included), org-wide or scoped.
/// </summary>
public static class ManagerGuard
{
    /// <param name="excludeRoleId">Compute AS IF this role granted nothing (role edit/delete simulation).</param>
    /// <param name="excludeAssignmentId">Compute AS IF this assignment were gone (unassign simulation).</param>
    public static async Task<HashSet<Guid>> ManagerUserIdsAsync(
        IdentityDbContext db,
        OrgId org,
        CancellationToken ct,
        Guid? excludeRoleId = null,
        Guid? excludeAssignmentId = null
    )
    {
        var userIds = await (
            from membership in db.Memberships
            where membership.OrgId == org
            join assignment in db.MembershipRoles on membership.Id equals assignment.MembershipId
            join grant in db.RoleGrants on assignment.RoleId equals grant.RoleId
            where
                (grant.Domain == "roles" || grant.Domain == "*")
                && (grant.Action == "manage" || grant.Action == "*")
                && (excludeRoleId == null || assignment.RoleId != excludeRoleId)
                && (excludeAssignmentId == null || assignment.Id != excludeAssignmentId)
            select membership.UserId
        )
            .Distinct()
            .ToListAsync(ct);
        return [.. userIds];
    }

    /// <summary>True when removing this user's management would orphan the org.</summary>
    public static async Task<bool> WouldOrphanAsync(
        IdentityDbContext db,
        OrgId org,
        Guid userId,
        CancellationToken ct
    )
    {
        var managers = await ManagerUserIdsAsync(db, org, ct);
        return managers.Contains(userId) && managers.Count == 1;
    }
}
