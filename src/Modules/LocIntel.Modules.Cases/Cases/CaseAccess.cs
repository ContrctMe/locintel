using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>What one request resolved about a case: the row, who is asking, and how far they reach.</summary>
public sealed record CaseAccess(Case Case, ActorRef Actor, bool Manage, bool Member)
{
    public bool CanWork => Manage || Member;

    /// <summary>
    /// Gates 2-4 for a case: cases:read scope (401 if none), visibility
    /// (404 if not), and the caller's standing on it. Managers also see the trash.
    /// </summary>
    public static async Task<(CaseAccess?, IResult?)> LoadAsync(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return (null, Results.Unauthorized());
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.CasesRead, ct);
        if (scope is NodeScope.None)
            return (null, Results.Unauthorized());
        var manage = await scopes.CanAsync(accessor.Current, Capabilities.CasesManage, ct);
        var visible = CaseVisibility.Visible(db, scope, accessor.Current, manage);
        if (manage)
            visible = visible.IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter]);
        var found = await visible.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (found is null)
            return (null, Results.NotFound());
        var member = await db.Members.AnyAsync(m => m.CaseId == id && m.UserId == actor.Id, ct);
        return (new CaseAccess(found, actor, manage, member), null);
    }
}
