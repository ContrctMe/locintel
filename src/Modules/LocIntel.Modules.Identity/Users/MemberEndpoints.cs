using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Users;

public sealed record InviteMemberRequest(string Email, Guid RoleId);

public sealed record MemberSummary(
    Guid UserId,
    string Email,
    string? Name,
    DateTimeOffset JoinedAt,
    string[] Roles
);

public sealed record MemberListResponse(
    IReadOnlyList<MemberSummary> Items,
    int Total,
    int? NextOffset
);

/// <summary>
/// Member management (day-zero arc): the provider (WorkOS) carries invitation
/// delivery, acceptance, and reminders; we carry role intent and the local
/// membership that authorization evaluates.
/// </summary>
public static class MemberEndpoints
{
    /// <summary>
    /// A member walks away from the ACTIVE org (deletion tier 3 + audit).
    /// The last role manager cannot leave - transfer management first.
    /// </summary>
    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/api/members/leave")]
    public static async Task<IResult> Leave(
        Microsoft.AspNetCore.Http.HttpContext http,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org, UserId: var userId })
            return Results.Unauthorized();
        var membership = await db.Memberships.FirstOrDefaultAsync(
            m => m.UserId == userId && m.OrgId == org,
            ct
        );
        if (membership is null)
            return Results.NotFound();
        if (await Access.ManagerGuard.WouldOrphanAsync(db, org, userId, ct))
            return Results.Conflict(
                new { error = "you are the last role manager; assign another before leaving" }
            );

        await db.MembershipRoles.Where(r => r.MembershipId == membership.Id).ExecuteDeleteAsync(ct);
        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new RecordDomainAudit(
                "member.left",
                System.Text.Json.JsonSerializer.Serialize(new { userId })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = userId.ToString(),
                },
            }
        );

        // session still points at the org they left: reissue against the next
        // membership (or none - back to the day-zero screen)
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var nextOrg = await db
            .Memberships.Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            // UUIDv7 tie-break: CreatedAt collides at Postgres microsecond
            // resolution for memberships created together - without this the
            // default org is a per-boot coin flip
            .ThenBy(m => m.Id)
            .Select(m => (OrgId?)m.OrgId)
            .FirstOrDefaultAsync(ct);
        await http.SignInAsync(
            Microsoft
                .AspNetCore
                .Authentication
                .Cookies
                .CookieAuthenticationDefaults
                .AuthenticationScheme,
            Auth.AuthEndpoints.BuildClaimsPrincipal(
                user,
                nextOrg,
                Auth.AuthEndpoints.GetSessionId(http.User)
            )
        );
        return Results.NoContent();
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineGet("/api/members")]
    [ProducesResponseType(typeof(MemberListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string? q,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();

        var query =
            from membership in db.Memberships
            where membership.OrgId == org
            join user in db.Users on membership.UserId equals user.Id
            select new
            {
                userId = user.Id,
                user.Email,
                user.Name,
                membershipId = membership.Id,
                joinedAt = membership.CreatedAt,
            };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(m =>
                EF.Functions.ILike(m.Email, pattern)
                || (m.Name != null && EF.Functions.ILike(m.Name, pattern))
            );
        }
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var members = await query
            .OrderBy(m => m.joinedAt)
            .ThenBy(m => m.membershipId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        var roleNames = await (
            from assignment in db.MembershipRoles
            join role in db.Roles on assignment.RoleId equals role.Id
            select new { assignment.MembershipId, role.Name }
        ).ToListAsync(ct);

        return Results.Ok(
            new MemberListResponse(
                members
                    .Select(m => new MemberSummary(
                        m.userId,
                        m.Email,
                        m.Name,
                        m.joinedAt,
                        roleNames
                            .Where(r => r.MembershipId == m.membershipId)
                            .Select(r => r.Name)
                            .ToArray()
                    ))
                    .ToList(),
                total,
                skip + members.Count < total ? skip + members.Count : null
            )
        );
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/api/members/invitations")]
    public static async Task<IResult> Invite(
        InviteMemberRequest request,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IAuthProvider provider,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var inviter } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        if (provider is not IOrganizationDirectory directory)
            return Results.Json(
                new { error = $"auth provider '{provider.Name}' does not support invitations" },
                statusCode: StatusCodes.Status501NotImplemented
            );
        var directoryEntry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == org, ct);
        if (directoryEntry?.ExternalId is not { } externalOrgId)
            return Results.Conflict(new { error = "org is not linked to the auth provider" });
        if (!await db.Roles.AnyAsync(r => r.Id == request.RoleId, ct))
            return Results.NotFound();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.InvitedRoles.AnyAsync(i => i.Email == email, ct))
            return Results.Conflict(
                new { error = "an invitation for this email is already pending" }
            );

        // provider delivers and tracks; we record what the invitee becomes
        var invitationId = await directory.SendInvitationAsync(externalOrgId, email, ct);
        db.InvitedRoles.Add(
            new InvitedRole
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                Email = email,
                RoleId = request.RoleId,
                InvitationExternalId = invitationId,
                InvitedBy = inviter,
            }
        );
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new RecordDomainAudit(
                "member.invited",
                System.Text.Json.JsonSerializer.Serialize(new { email })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = inviter.ToString(),
                },
            }
        );
        return Results.Ok(new { invitationId });
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineGet("/api/members/invitations")]
    public static async Task<IResult> ListInvitations(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IAuthProvider provider,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        var directoryEntry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == org, ct);
        if (
            provider is not IOrganizationDirectory directory
            || directoryEntry?.ExternalId is not { } externalOrgId
        )
            return Results.Ok(Array.Empty<object>());

        var pending = await directory.ListInvitationsAsync(externalOrgId, ct);
        var intents = await db.InvitedRoles.ToListAsync(ct);
        var roles = await db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        return Results.Ok(
            pending.Select(p => new
            {
                id = p.Id,
                email = p.Email,
                state = p.State,
                expiresAt = p.ExpiresAt,
                role = intents.FirstOrDefault(i => i.InvitationExternalId == p.Id) is { } intent
                    ? roles.GetValueOrDefault(intent.RoleId)
                    : null,
            })
        );
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineDelete("/api/members/invitations/{invitationId}")]
    public static async Task<IResult> Revoke(
        string invitationId,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IAuthProvider provider,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: not null } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        var intent = await db.InvitedRoles.FirstOrDefaultAsync(
            i => i.InvitationExternalId == invitationId,
            ct
        );
        if (intent is null)
            return Results.NotFound();
        if (provider is IOrganizationDirectory directory)
            await directory.RevokeInvitationAsync(invitationId, ct);
        db.InvitedRoles.Remove(intent);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineDelete("/api/members/{userId}")]
    public static async Task<IResult> Remove(
        Guid userId,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var actor } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        if (userId == actor)
            return Results.BadRequest(
                new { error = "you cannot remove yourself; use leave instead" }
            );

        var membership = await db.Memberships.FirstOrDefaultAsync(
            m => m.UserId == userId && m.OrgId == org,
            ct
        );
        if (membership is null)
            return Results.NotFound();
        if (await Access.ManagerGuard.WouldOrphanAsync(db, org, userId, ct))
            return Results.Conflict(new { error = "cannot remove the org's last role manager" });

        // deletion tier 3 (ADR 25): the membership ends; audit is the record
        await db.MembershipRoles.Where(r => r.MembershipId == membership.Id).ExecuteDeleteAsync(ct);
        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new RecordDomainAudit(
                "member.removed",
                System.Text.Json.JsonSerializer.Serialize(new { userId })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = actor.ToString(),
                },
            }
        );
        return Results.NoContent();
    }
}
