using LocIntel.Contracts;
using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares.Api;
using LocIntel.Modules.Network.Shares.Messages;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Network.Shares;

/// <summary>
/// Shares, on rows each org OWNS (ADR 48): the owner acts on the share and
/// its roster; a member acts on its own access row and tells the owner.
/// Every org reads a share from its access row - the owner's projection of
/// it. Gate 1 is network.enabled for creating and inviting; network:read
/// sees the org's shares; network:manage acts.
/// </summary>
public static class ShareEndpoints
{
    [Transactional(
        typeof(NetworkDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkRead, ct);
        if (gate.Actor is null)
            return gate.ToResult();
        var now = time.GetUtcNow();
        var rows = await db
            .Access.Where(a => a.Status != MembershipStatus.Removed)
            .OrderBy(a => a.Status)
            .ThenBy(a => a.ShareName)
            .ToListAsync(ct);
        var items = new List<ShareSummary>();
        foreach (var a in rows)
            items.Add(await SummaryAsync(a, db, now, ct));
        return Results.Ok(new ShareListResponse(items));
    }

    [Transactional(
        typeof(NetworkDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkRead, ct);
        if (gate.Actor is null)
            return gate.ToResult();
        var access = await LoadAsync(id, db, ct);
        if (access is null)
            return Results.NotFound();
        // the owner reads its roster live; a member reads the snapshot the owner published
        var roster =
            access.Role == MemberRole.Owner
                ? (
                    await db
                        .Members.Where(m => m.ShareId == id)
                        .OrderBy(m => m.CreatedAt)
                        .ToListAsync(ct)
                )
                    .Select(m => m.Entry())
                    .ToList()
                : access.Roster();
        return Results.Ok(
            new ShareDetail(
                await SummaryAsync(access, db, time.GetUtcNow(), ct),
                roster
                    .Select(m => new MemberView(m.OrgId, m.OrgName, m.Role, m.Status, m.JoinedAt))
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        if (await Upsell(entitlements, actor.Org, ct) is { } upsell)
            return upsell;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            return ApiErrors.BadRequest("a share needs a name of up to 200 characters");
        var owner = await orgs.GetAsync(actor.Org, ct);
        var ownerName = owner?.Name ?? "Owner";
        var share = new Share
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            OwnerName = ownerName,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? "",
            CreatedBy = actor.Id,
        };
        var now = DateTimeOffset.UtcNow;
        var self = new ShareMember
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            ShareId = share.Id,
            MemberOrgId = actor.Org,
            OrgName = ownerName,
            Role = MemberRole.Owner,
            Status = MembershipStatus.Active,
            JoinedAt = now,
        };
        db.Shares.Add(share);
        db.Members.Add(self);
        db.Access.Add(
            new ShareAccess
            {
                Id = Guid.CreateVersion7(),
                OrgId = actor.Org,
                ShareId = share.Id,
                OwnerOrgId = actor.Org,
                ShareName = share.Name,
                Description = share.Description,
                OwnerName = ownerName,
                Role = MemberRole.Owner,
                Status = MembershipStatus.Active,
                JoinedAt = now,
                RosterJson = ShareRoster.Json([self]),
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        if (await Upsell(entitlements, actor.Org, ct) is { } upsell)
            return upsell;
        var (share, error) = await OwnedAsync(id, db, ct);
        if (error is not null)
            return error;
        if (share!.Status == ShareStatus.Closed)
            return ApiErrors.Conflict("the share is closed");
        var invitee = await orgs.FindBySlugAsync(request.Slug?.Trim().ToLowerInvariant() ?? "", ct);
        if (invitee is null)
            return ApiErrors.NotFound("no organization has that slug");
        if (invitee.Id == actor.Org)
            return ApiErrors.BadRequest("your org already owns this share");
        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ShareId == id && m.MemberOrgId == invitee.Id,
            ct
        );
        if (member is { Status: MembershipStatus.Active or MembershipStatus.Invited })
            return ApiErrors.Conflict("already invited or a member");
        if (member is null)
            db.Members.Add(
                new ShareMember
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = actor.Org,
                    ShareId = id,
                    MemberOrgId = invitee.Id,
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
        // the invitee's own access row is created under THEIR tenant, with the projection
        var roster = ShareRoster.Json(await db.Members.Where(m => m.ShareId == id).ToListAsync(ct));
        await bus.PublishForOrgAsync(
            invitee.Id,
            new ShareInvitationRequested(
                share.Id,
                share.OrgId,
                share.Name,
                share.Description,
                share.OwnerName,
                share.Status,
                roster,
                actor.Id
            )
        );
        await ShareRoster.PublishAsync(db, bus, share, ct);
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var access = await LoadAsync(id, db, ct);
        if (access is null)
            return Results.NotFound();
        if (access.Status != MembershipStatus.Invited)
            return ApiErrors.Conflict("no pending invitation");
        var now = DateTimeOffset.UtcNow;
        access.Status = MembershipStatus.Active;
        access.JoinedAt = now;
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(
            access.OwnerOrgId,
            new ShareMembershipChanged(id, actor.Org, MembershipStatus.Active, now)
        );
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var access = await LoadAsync(id, db, ct);
        if (access is null)
            return Results.NotFound();
        if (access.Role == MemberRole.Owner)
            return ApiErrors.Conflict("the owner closes a share instead of leaving it");
        access.Status = MembershipStatus.Left;
        // leaving is deletion of what the share gave you (ADR 48)
        await db.BulletinCopies.Where(c => c.ShareId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(
            access.OwnerOrgId,
            new ShareMembershipChanged(id, actor.Org, MembershipStatus.Left, null)
        );
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var (share, error) = await OwnedAsync(id, db, ct);
        if (error is not null)
            return error;
        var target = new OrgId(orgId);
        if (target == actor.Org)
            return ApiErrors.BadRequest("the owner cannot be removed");
        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ShareId == id && m.MemberOrgId == target,
            ct
        );
        if (member is null || member.Status == MembershipStatus.Removed)
            return Results.NotFound();
        member.Status = MembershipStatus.Removed;
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(target, new ShareAccessRevoked(id));
        await ShareRoster.PublishAsync(db, bus, share!, ct);
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var (share, error) = await OwnedAsync(id, db, ct);
        if (error is not null)
            return error;
        share!.Status = ShareStatus.Closed;
        share.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await ShareRoster.PublishAsync(db, bus, share, ct);
        await bus.AuditAsync(actor.Org, actor.Audit, "network.share_closed", new { ShareId = id });
        var access = await LoadAsync(id, db, ct);
        return Results.Ok(new ShareMutated(id, access?.Status ?? MembershipStatus.Active));
    }

    /// <summary>The caller's own standing in the share, if any: what every read hangs off.</summary>
    internal static Task<ShareAccess?> LoadAsync(
        Guid id,
        NetworkDbContext db,
        CancellationToken ct
    ) =>
        db.Access.FirstOrDefaultAsync(
            a => a.ShareId == id && a.Status != MembershipStatus.Removed,
            ct
        );

    /// <summary>The share itself, which only its owner holds: 404 for a member (it is not theirs), 403 never - the row simply is not in their tenant.</summary>
    private static async Task<(Share?, IResult?)> OwnedAsync(
        Guid id,
        NetworkDbContext db,
        CancellationToken ct
    )
    {
        var share = await db.Shares.FirstOrDefaultAsync(s => s.Id == id, ct);
        return share is null ? (null, Results.NotFound()) : (share, null);
    }

    private static async Task<ShareSummary> SummaryAsync(
        ShareAccess a,
        NetworkDbContext db,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var active =
            await db.Bulletins.CountAsync(
                b => b.ShareId == a.ShareId && b.WithdrawnAt == null && b.ExpiresAt > now,
                ct
            )
            + await db.BulletinCopies.CountAsync(
                c => c.ShareId == a.ShareId && c.WithdrawnAt == null && c.ExpiresAt > now,
                ct
            );
        return new ShareSummary(
            a.ShareId,
            a.ShareName,
            a.Description,
            a.OwnerName,
            a.Role == MemberRole.Owner,
            a.Role,
            a.Status,
            a.ShareStatus,
            a.Roster().Count(m => m.Status == MembershipStatus.Active),
            active,
            a.CreatedAt
        );
    }

    internal static async Task<IResult?> Upsell(
        IEntitlements entitlements,
        OrgId org,
        CancellationToken ct
    ) =>
        await entitlements.HasAsync(org, EntitlementCatalog.NetworkEnabled, ct)
            ? null
            : GateResults.FeatureOff(EntitlementCatalog.NetworkEnabled);
}
