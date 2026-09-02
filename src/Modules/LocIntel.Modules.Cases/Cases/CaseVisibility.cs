using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>
/// Need-to-know for cases: a member of the case, or a case holding an
/// incident whose stamped path lies in the reader's cases:read scope, or
/// cases:manage. Filters; never fails.
/// </summary>
public static class CaseVisibility
{
    public static IQueryable<Case> Visible(
        CasesDbContext db,
        NodeScope scope,
        Principal principal,
        bool manage
    )
    {
        if (manage)
            return db.Cases;
        var incidents = db.Incidents.InScope(scope);
        var userId = principal is Principal.User user ? user.UserId : Guid.Empty;
        return db.Cases.Where(c =>
            db.Members.Any(m => m.CaseId == c.Id && m.UserId == userId)
            || incidents.Any(i => i.CaseId == c.Id)
        );
    }
}
