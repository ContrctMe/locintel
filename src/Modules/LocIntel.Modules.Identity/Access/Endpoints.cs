using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Access;

public sealed record CreateRoleRequest(string Name, GrantSpec[] Grants);

public sealed record GrantSpec(string Domain, string Action);

public sealed record AssignRoleRequest(Guid UserId, string? ScopePath = null);

public sealed record AddGrantExceptionRequest(
    Guid UserId,
    string Domain,
    string Action,
    string Reason,
    DateTimeOffset ExpiresAt,
    string? ScopePath = null
);

public static class AccessEndpoints
{
    [Wolverine.Attributes.Transactional(typeof(IdentityDbContext))]
    [WolverineGet("/api/roles")]
    public static async Task<IResult> ListRoles(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.RolesManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal })
            return gate.ToResult();
        // the editor's read: grants and reach, not just names
        var roles = await db
            .Roles.OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                grants = db
                    .RoleGrants.Where(g => g.RoleId == r.Id)
                    .Select(g => new { g.Domain, g.Action })
                    .ToList(),
                assignedCount = db.MembershipRoles.Count(mr => mr.RoleId == r.Id),
            })
            .ToListAsync(ct);
        return Results.Ok(roles);
    }

    [WolverinePost("/api/roles")]
    public static async Task<IResult> CreateRole(
        CreateRoleRequest request,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.RolesManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();

        var role = Role.Create(org, request.Name);
        db.Roles.Add(role);
        foreach (var grant in request.Grants)
            db.RoleGrants.Add(
                new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org,
                    RoleId = role.Id,
                    Domain = grant.Domain,
                    Action = grant.Action,
                }
            );
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { role.Id });
    }

    [WolverinePost("/api/roles/{id}/assign")]
    public static async Task<IResult> Assign(
        Guid id,
        AssignRoleRequest request,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.RolesManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        var membership = await db.Memberships.FirstOrDefaultAsync(
            m => m.UserId == request.UserId && m.OrgId == org,
            ct
        );
        if (role is null || membership is null)
            return Results.NotFound();

        db.MembershipRoles.Add(
            new MembershipRole
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                MembershipId = membership.Id,
                RoleId = role.Id,
                ScopePath = request.ScopePath,
            }
        );
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    [WolverinePost("/api/grant-exceptions")]
    public static async Task<IResult> AddException(
        AddGrantExceptionRequest request,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.RolesManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var grantor = principal.UserId;
        if (request.ExpiresAt <= DateTimeOffset.UtcNow)
            return Results.BadRequest(new { error = "exceptions must expire in the future" });
        if (!await db.Memberships.AnyAsync(m => m.UserId == request.UserId && m.OrgId == org, ct))
            return Results.NotFound();

        db.GrantExceptions.Add(
            new GrantException
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                UserId = request.UserId,
                Domain = request.Domain,
                Action = request.Action,
                ScopePath = request.ScopePath,
                Reason = request.Reason,
                GrantedBy = grantor,
                ExpiresAt = request.ExpiresAt,
            }
        );
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
