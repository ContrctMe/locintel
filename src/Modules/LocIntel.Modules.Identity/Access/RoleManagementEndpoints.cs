using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Access;

public sealed record UpdateRoleRequest(string Name, GrantSpec[] Grants);

/// <summary>Role lifecycle beyond create+assign: edit, unassign, delete - all behind the last-manager guard.</summary>
public static class RoleManagementEndpoints
{
    [Transactional(typeof(IdentityDbContext))]
    [WolverinePut("/api/roles/{id}")]
    public static async Task<IResult> Update(
        Guid id,
        UpdateRoleRequest request,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            return Results.NotFound();
        if (request.Grants.Length == 0)
            return Results.BadRequest(new { error = "a role needs at least one grant" });

        // PRE-check (the transaction commits on any normal result): would this
        // edit leave the org without anyone able to manage roles?
        var newGrantsManage = request.Grants.Any(g =>
            g.Domain is "roles" or "*" && g.Action is "manage" or "*"
        );
        var managersElsewhere = await ManagerGuard.ManagerUserIdsAsync(
            db,
            org,
            ct,
            excludeRoleId: id
        );
        if (!newGrantsManage && managersElsewhere.Count == 0)
            return Results.Conflict(
                new { error = "this edit would leave the org without anyone able to manage roles" }
            );

        role.Name = request.Name;
        await db.RoleGrants.Where(g => g.RoleId == id).ExecuteDeleteAsync(ct);
        foreach (var grant in request.Grants)
            db.RoleGrants.Add(
                new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org,
                    RoleId = id,
                    Domain = grant.Domain,
                    Action = grant.Action,
                }
            );
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineDelete("/api/roles/{id}")]
    public static async Task<IResult> Delete(
        Guid id,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: not null } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            return Results.NotFound();
        if (await db.MembershipRoles.AnyAsync(m => m.RoleId == id, ct))
            return Results.Conflict(new { error = "unassign this role from all members first" });
        if (await db.InvitedRoles.AnyAsync(i => i.RoleId == id, ct))
            return Results.Conflict(new { error = "a pending invitation references this role" });

        await db.RoleGrants.Where(g => g.RoleId == id).ExecuteDeleteAsync(ct);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineDelete("/api/roles/{id}/assign/{userId}")]
    public static async Task<IResult> Unassign(
        Guid id,
        Guid userId,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        var membership = await db.Memberships.FirstOrDefaultAsync(
            m => m.UserId == userId && m.OrgId == org,
            ct
        );
        if (membership is null)
            return Results.NotFound();
        var assignment = await db.MembershipRoles.FirstOrDefaultAsync(
            m => m.MembershipId == membership.Id && m.RoleId == id,
            ct
        );
        if (assignment is null)
            return Results.NotFound();

        var remaining = await ManagerGuard.ManagerUserIdsAsync(
            db,
            org,
            ct,
            excludeAssignmentId: assignment.Id
        );
        if (remaining.Count == 0)
            return Results.Conflict(new { error = "cannot unassign the org's last role manager" });

        db.MembershipRoles.Remove(assignment);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineGet("/api/grant-exceptions")]
    public static async Task<IResult> ListExceptions(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: not null } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        var now = DateTimeOffset.UtcNow;
        var exceptions = await (
            from exception in db.GrantExceptions
            where exception.ExpiresAt > now
            join user in db.Users on exception.UserId equals user.Id
            orderby exception.ExpiresAt
            select new
            {
                exception.Id,
                email = user.Email,
                exception.Domain,
                exception.Action,
                exception.ScopePath,
                exception.Reason,
                exception.ExpiresAt,
            }
        ).ToListAsync(ct);
        return Results.Ok(exceptions);
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineDelete("/api/grant-exceptions/{id}")]
    public static async Task<IResult> RevokeException(
        Guid id,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: not null } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        var exception = await db.GrantExceptions.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exception is null)
            return Results.NotFound();
        db.GrantExceptions.Remove(exception);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
