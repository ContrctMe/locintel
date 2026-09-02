using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Network.Bulletins;
using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares.Api;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Network.Shares;

/// <summary>
/// Shares: create (owner), invite by slug, accept, leave, remove. Gate 1 is
/// network.enabled for creating and inviting; network:read sees the org's
/// shares; network:manage acts. Every membership change is a domain event.
/// </summary>
public static class ShareEndpoints
{
    [Transactional(typeof(NetworkDbContext))]
    [WolverineGet("/api/network/shares")]
    [ProducesResponseType(typeof(ShareListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkRead, ct))
            return Results.Unauthorized();
        var now = time.GetUtcNow();
        var rows = await (
            from a in db.Access
            join s in db.Shares on a.ShareId equals s.Id
            where a.Status != MembershipStatus.Removed
            orderby a.Status, s.Name
            select new ShareSummary(
                s.Id,
                s.Name,
                s.Description,
                s.OwnerName,
                s.OwnerOrgId == actor.Org,
                a.Role,
                a.Status,
                s.Status,
                db.Members.Count(m => m.ShareId == s.Id && m.Status == MembershipStatus.Active),
                db.Bulletins.Count(b =>
                    b.ShareId == s.Id && b.WithdrawnAt == null && b.ExpiresAt > now
                ),
                s.CreatedAt
            )
        ).ToListAsync(ct);
        return Results.Ok(new ShareListResponse(rows));
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverineGet("/api/network/shares/{id}")]
    [ProducesResponseType(typeof(ShareDetail), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkRead, ct))
            return Results.Unauthorized();
        var (share, access) = await Load(id, db, ct);
        if (share is null)
            return Results.NotFound();
        var now = time.GetUtcNow();
        var members = await db
            .Members.Where(m => m.ShareId == id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
        return Results.Ok(
            new ShareDetail(
                new ShareSummary(
                    share.Id,
                    share.Name,
                    share.Description,
                    share.OwnerName,
                    share.OwnerOrgId == actor.Org,
                    access!.Role,
                    access.Status,
                    share.Status,
                    members.Count(m => m.Status == MembershipStatus.Active),
                    await db.Bulletins.CountAsync(
                        b => b.ShareId == id && b.WithdrawnAt == null && b.ExpiresAt > now,
                        ct
                    ),
                    share.CreatedAt
                ),
                members
                    .Select(m => new MemberView(
                        m.OrgId.Value,
                        m.OrgName,
                        m.Role,
                        m.Status,
                        m.JoinedAt
                    ))
                    .ToList()
            )
        );
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverinePost("/api/network/shares")]
    [ProducesResponseType(typeof(ShareCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateShareRequest request,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        IOrganizationLookup orgs,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkManage, ct))
            return Results.Unauthorized();
        if (await Upsell(entitlements, actor.Org, ct) is { } upsell)
            return upsell;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            return Results.BadRequest(
                new { error = "a share needs a name of up to 200 characters" }
            );
        var owner = await orgs.GetAsync(actor.Org, ct);
        var ownerName = owner?.Name ?? "Owner";
        var share = new Share
        {
            Id = Guid.CreateVersion7(),
            OwnerOrgId = actor.Org,
            OwnerName = ownerName,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? "",
            CreatedBy = actor.Id,
        };
        var now = DateTimeOffset.UtcNow;
        db.Shares.Add(share);
        db.Access.Add(
            new ShareAccess
            {
                Id = Guid.CreateVersion7(),
                OrgId = actor.Org,
                ShareId = share.Id,
                ShareName = share.Name,
                Role = MemberRole.Owner,
                Status = MembershipStatus.Active,
                JoinedAt = now,
            }
        );
        db.Members.Add(
            new ShareMember
            {
                Id = Guid.CreateVersion7(),
                ShareId = share.Id,
                OrgId = actor.Org,
                OrgName = ownerName,
                Role = MemberRole.Owner,
                Status = MembershipStatus.Active,
                JoinedAt = now,
            }
        );
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "network.share_created",
            new { share.Id, share.Name }
        );
        return Results.Ok(new ShareCreated(share.Id));
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverinePost("/api/network/shares/{id}/invite")]
    [ProducesResponseType(typeof(MemberInvited), StatusCodes.Status200OK)]
    public static async Task<IResult> Invite(
        Guid id,
        InviteOrgRequest request,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        IOrganizationLookup orgs,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkManage, ct))
            return Results.Unauthorized();
        if (await Upsell(entitlements, actor.Org, ct) is { } upsell)
            return upsell;
        var (share, access) = await Load(id, db, ct);
        if (share is null)
            return Results.NotFound();
        if (share.OwnerOrgId != actor.Org)
            return Results.Unauthorized();
        if (share.Status == ShareStatus.Closed)
            return Results.Conflict(new { error = "the share is closed" });
        var invitee = await orgs.FindBySlugAsync(request.Slug?.Trim().ToLowerInvariant() ?? "", ct);
        if (invitee is null)
            return Results.NotFound(new { error = "no organization has that slug" });
        if (invitee.Id == actor.Org)
            return Results.BadRequest(new { error = "your org already owns this share" });
        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ShareId == id && m.OrgId == invitee.Id,
            ct
        );
        if (member is { Status: MembershipStatus.Active or MembershipStatus.Invited })
            return Results.Conflict(new { error = "already invited or a member" });
        if (member is null)
            db.Members.Add(
                new ShareMember
                {
                    Id = Guid.CreateVersion7(),
                    ShareId = id,
                    OrgId = invitee.Id,
                    OrgName = invitee.Name,
                    Role = MemberRole.Member,
                }
            );
        else
        {
            member.Status = MembershipStatus.Invited;
            member.JoinedAt = null;
        }
        await db.SaveChangesAsync(ct);
        // the invitee's own access row is created under THEIR tenant
        await bus.PublishAsync(
            new ShareInvitationRequested(share.Id, share.Name, share.OwnerName, actor.Id),
            new DeliveryOptions { TenantId = invitee.Id.Value.ToString() }
        );
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "network.member_invited",
            new
            {
                ShareId = id,
                OrgId = invitee.Id.Value,
                invitee.Slug,
            }
        );
        return Results.Ok(new MemberInvited(invitee.Id.Value, invitee.Name));
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverinePost("/api/network/shares/{id}/accept")]
    [ProducesResponseType(typeof(ShareMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Accept(
        Guid id,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkManage, ct))
            return Results.Unauthorized();
        var (share, access) = await Load(id, db, ct);
        if (share is null)
            return Results.NotFound();
        if (access!.Status != MembershipStatus.Invited)
            return Results.Conflict(new { error = "no pending invitation" });
        var now = DateTimeOffset.UtcNow;
        access.Status = MembershipStatus.Active;
        access.JoinedAt = now;
        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ShareId == id && m.OrgId == actor.Org,
            ct
        );
        if (member is not null)
        {
            member.Status = MembershipStatus.Active;
            member.JoinedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(actor.Org, actor.Audit, "network.share_joined", new { ShareId = id });
        return Results.Ok(new ShareMutated(id, access.Status));
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverinePost("/api/network/shares/{id}/leave")]
    [ProducesResponseType(typeof(ShareMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Leave(
        Guid id,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkManage, ct))
            return Results.Unauthorized();
        var (share, access) = await Load(id, db, ct);
        if (share is null)
            return Results.NotFound();
        if (share.OwnerOrgId == actor.Org)
            return Results.Conflict(
                new { error = "the owner closes a share instead of leaving it" }
            );
        access!.Status = MembershipStatus.Left;
        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ShareId == id && m.OrgId == actor.Org,
            ct
        );
        if (member is not null)
            member.Status = MembershipStatus.Left;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(actor.Org, actor.Audit, "network.share_left", new { ShareId = id });
        return Results.Ok(new ShareMutated(id, access.Status));
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverineDelete("/api/network/shares/{id}/members/{orgId}")]
    [ProducesResponseType(typeof(MemberRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Remove(
        Guid id,
        Guid orgId,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkManage, ct))
            return Results.Unauthorized();
        var (share, _) = await Load(id, db, ct);
        if (share is null)
            return Results.NotFound();
        if (share.OwnerOrgId != actor.Org)
            return Results.Unauthorized();
        var target = new OrgId(orgId);
        if (target == actor.Org)
            return Results.BadRequest(new { error = "the owner cannot be removed" });
        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ShareId == id && m.OrgId == target,
            ct
        );
        if (member is null || member.Status == MembershipStatus.Removed)
            return Results.NotFound();
        member.Status = MembershipStatus.Removed;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ShareAccessRevoked(id),
            new DeliveryOptions { TenantId = target.Value.ToString() }
        );
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "network.member_removed",
            new { ShareId = id, OrgId = orgId }
        );
        return Results.Ok(new MemberRemoved(orgId));
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverinePost("/api/network/shares/{id}/close")]
    [ProducesResponseType(typeof(ShareMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Close(
        Guid id,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkManage, ct))
            return Results.Unauthorized();
        var (share, access) = await Load(id, db, ct);
        if (share is null)
            return Results.NotFound();
        if (share.OwnerOrgId != actor.Org)
            return Results.Unauthorized();
        share.Status = ShareStatus.Closed;
        share.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(actor.Org, actor.Audit, "network.share_closed", new { ShareId = id });
        return Results.Ok(new ShareMutated(id, access!.Status));
    }

    internal static async Task<(Share?, ShareAccess?)> Load(
        Guid id,
        NetworkDbContext db,
        CancellationToken ct
    )
    {
        var access = await db.Access.FirstOrDefaultAsync(
            a => a.ShareId == id && a.Status != MembershipStatus.Removed,
            ct
        );
        if (access is null)
            return (null, null);
        var share = await db.Shares.FirstOrDefaultAsync(s => s.Id == id, ct);
        return share is null ? (null, null) : (share, access);
    }

    internal static async Task<IResult?> Upsell(
        IEntitlements entitlements,
        OrgId org,
        CancellationToken ct
    ) =>
        await entitlements.HasAsync(org, EntitlementCatalog.NetworkEnabled, ct)
            ? null
            : Results.Json(
                new
                {
                    error = "intelligence sharing is not part of this plan",
                    code = EntitlementCatalog.NetworkEnabled,
                },
                statusCode: StatusCodes.Status402PaymentRequired
            );
}
