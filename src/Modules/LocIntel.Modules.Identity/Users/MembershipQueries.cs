using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Identity.Users;

public static class MembershipQueries
{
    /// <summary>
    /// The org a user lands in when none is chosen: the oldest membership.
    /// UUIDv7 tie-break, because CreatedAt collides at Postgres microsecond
    /// resolution for memberships created together - without it the default
    /// org is a per-boot coin flip. Login, leave-org and impersonation-stop
    /// all need this rule, and each had its own copy of it.
    /// </summary>
    public static Task<OrgId?> DefaultOrgAsync(
        this IdentityDbContext db,
        Guid userId,
        CancellationToken ct
    ) =>
        db
            .Memberships.Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => (OrgId?)m.OrgId)
            .FirstOrDefaultAsync(ct);
}
