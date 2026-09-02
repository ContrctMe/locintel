using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Contracts.Entities;
using LocIntel.Modules.Network.Bulletins.Api;
using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Network.Bulletins;

/// <summary>
/// What flows through a share: copies, published by an active member from
/// what THEY may see, readable by every active member, withdrawn only by
/// the publisher, importable by any other member as their own record.
/// </summary>
public static class SharedBulletinEndpoints
{
    public const int DefaultDays = 30;
    public const int MaxDays = 180;

    [Transactional(typeof(NetworkDbContext))]
    [WolverineGet("/api/network/bulletins")]
    [ProducesResponseType(typeof(SharedBulletinListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        Guid? shareId,
        bool? includeInactive,
        string? q,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkRead, ct))
            return Results.Unauthorized();
        var now = time.GetUtcNow();
        var query = db.Bulletins.AsQueryable();
        if (shareId is { } share)
            query = query.Where(b => b.ShareId == share);
        if (includeInactive is not true)
            query = query.Where(b => b.WithdrawnAt == null && b.ExpiresAt > now);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(b =>
                EF.Functions.ILike(b.Title, needle)
                || EF.Functions.ILike(b.Body, needle)
                || (b.DisplayName != null && EF.Functions.ILike(b.DisplayName, needle))
                || b.Aliases.Any(a => EF.Functions.ILike(a, needle))
            );
        }
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var rows = await query
            .OrderByDescending(b => b.PublishedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        var shareIds = rows.Select(r => r.ShareId).Distinct().ToArray();
        var names = await db
            .Access.Where(a => shareIds.Contains(a.ShareId))
            .ToDictionaryAsync(a => a.ShareId, a => a.ShareName, ct);
        var items = rows.Select(b =>
                View(b, actor.Org, names.GetValueOrDefault(b.ShareId, ""), now)
            )
            .ToList();
        return Results.Ok(
            new SharedBulletinListResponse(
                items,
                total,
                skip + items.Count < total ? skip + items.Count : null
            )
        );
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverinePost("/api/network/shares/{id}/bulletins")]
    [ProducesResponseType(typeof(SharedBulletinPublished), StatusCodes.Status200OK)]
    public static async Task<IResult> Publish(
        Guid id,
        PublishBulletinRequest request,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        IEntityDirectory entities,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkManage, ct))
            return Results.Unauthorized();
        if (await ShareEndpoints.Upsell(entitlements, actor.Org, ct) is { } upsell)
            return upsell;
        var (share, access) = await ShareEndpoints.Load(id, db, ct);
        if (share is null)
            return Results.NotFound();
        if (access!.Status != MembershipStatus.Active)
            return Results.Conflict(new { error = "accept the invitation before publishing" });
        if (share.Status == ShareStatus.Closed)
            return Results.Conflict(new { error = "the share is closed" });
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return Results.BadRequest(
                new { error = "a bulletin needs a title of up to 200 characters" }
            );
        if (string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest(new { error = "a bulletin needs a body" });
        var now = time.GetUtcNow();
        var expiresAt = request.ExpiresAt ?? now.AddDays(DefaultDays);
        if (expiresAt <= now || expiresAt > now.AddDays(MaxDays))
            return Results.BadRequest(
                new { error = $"a bulletin expires between now and {MaxDays} days out" }
            );

        EntityInfo? entity = null;
        if (request.EntityId is { } entityId)
        {
            var visible = await entities.LookupVisibleAsync([entityId], ct);
            if (!visible.TryGetValue(entityId, out entity))
                return Results.NotFound();
        }
        var publisher =
            await db
                .Members.Where(m => m.ShareId == id && m.OrgId == actor.Org)
                .Select(m => m.OrgName)
                .FirstOrDefaultAsync(ct)
            ?? "Member";
        var bulletin = new SharedBulletin
        {
            Id = Guid.CreateVersion7(),
            ShareId = id,
            PublisherOrgId = actor.Org,
            PublisherName = publisher,
            Kind = request.Kind,
            Severity = request.Severity,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            EntityKind = entity?.Kind,
            DisplayName = entity?.DisplayName,
            Aliases = entity?.Aliases ?? [],
            DescriptorsJson = JsonSerializer.Serialize(
                entity?.Descriptors ?? new Dictionary<string, string>()
            ),
            Areas = (request.Areas ?? [])
                .Select(a => a.Trim().ToUpperInvariant())
                .Where(a => a.Length > 0)
                .Distinct()
                .Take(20)
                .ToArray(),
            SourceEntityId = entity?.Id,
            PublishedBy = actor.Id,
            ExpiresAt = expiresAt,
        };
        db.Bulletins.Add(bulletin);
        await db.SaveChangesAsync(ct);
        var recipients = await db
            .Members.Where(m =>
                m.ShareId == id && m.Status == MembershipStatus.Active && m.OrgId != actor.Org
            )
            .Select(m => m.OrgId)
            .ToListAsync(ct);
        foreach (var org in recipients)
            await bus.PublishAsync(
                new SendOrgNotice(
                    $"Shared {bulletin.Kind.ToString().ToUpperInvariant()} in {share.Name}: {bulletin.Title}",
                    [
                        $"From {publisher}.",
                        bulletin.Body,
                        "Open Network in the console to review or import it.",
                    ],
                    "network",
                    $"Shared {bulletin.Kind.ToString().ToUpperInvariant()} in {share.Name}: {bulletin.Title}"
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
        await ShareEndpoints.Audit(
            bus,
            actor,
            "network.bulletin_published",
            new
            {
                bulletin.Id,
                ShareId = id,
                Kind = bulletin.Kind.ToString(),
                HasEntity = entity != null,
            }
        );
        return Results.Ok(new SharedBulletinPublished(bulletin.Id, bulletin.ExpiresAt));
    }

    [Transactional(typeof(NetworkDbContext))]
    [WolverinePost("/api/network/bulletins/{id}/withdraw")]
    [ProducesResponseType(typeof(SharedBulletinMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Withdraw(
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
        var bulletin = await db.Bulletins.FirstOrDefaultAsync(
            b => b.Id == id && b.PublisherOrgId == actor.Org,
            ct
        );
        if (bulletin is null)
            return Results.NotFound();
        if (bulletin.WithdrawnAt is null)
        {
            bulletin.WithdrawnAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await ShareEndpoints.Audit(
                bus,
                actor,
                "network.bulletin_withdrawn",
                new { bulletin.Id }
            );
        }
        return Results.Ok(new SharedBulletinMutated(bulletin.Id, bulletin.WithdrawnAt));
    }

    /// <summary>Copy a shared record into your own People &amp; vehicles (over the outbox, under your tenant).</summary>
    [Transactional(typeof(NetworkDbContext))]
    [WolverinePost("/api/network/bulletins/{id}/import")]
    [ProducesResponseType(typeof(ImportQueued), StatusCodes.Status200OK)]
    public static async Task<IResult> Import(
        Guid id,
        NetworkDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.NetworkManage, ct))
            return Results.Unauthorized();
        var now = time.GetUtcNow();
        var bulletin = await db.Bulletins.FirstOrDefaultAsync(
            b => b.Id == id && b.WithdrawnAt == null && b.ExpiresAt > now,
            ct
        );
        if (bulletin is null)
            return Results.NotFound();
        if (bulletin.PublisherOrgId == actor.Org)
            return Results.Conflict(new { error = "this is your own record" });
        if (bulletin.DisplayName is null)
            return Results.Conflict(new { error = "this bulletin carries no record to import" });
        var shareName =
            await db
                .Access.Where(a => a.ShareId == bulletin.ShareId)
                .Select(a => a.ShareName)
                .FirstOrDefaultAsync(ct)
            ?? "a share";
        await bus.PublishAsync(
            new ImportEntityRequested(
                bulletin.EntityKind ?? "Person",
                bulletin.DisplayName,
                bulletin.Aliases,
                JsonSerializer.Deserialize<Dictionary<string, string>>(bulletin.DescriptorsJson)
                    ?? [],
                $"Imported from \"{shareName}\", published by {bulletin.PublisherName} on {bulletin.PublishedAt:yyyy-MM-dd}: {bulletin.Title}. {bulletin.Body}",
                actor.Id
            ),
            new DeliveryOptions { TenantId = actor.Org.Value.ToString() }
        );
        await ShareEndpoints.Audit(
            bus,
            actor,
            "network.bulletin_imported",
            new { bulletin.Id, bulletin.ShareId }
        );
        return Results.Ok(new ImportQueued(bulletin.Id));
    }

    private static SharedBulletinView View(
        SharedBulletin b,
        OrgId reader,
        string shareName,
        DateTimeOffset now
    ) =>
        new(
            b.Id,
            b.ShareId,
            shareName,
            b.PublisherOrgId.Value,
            b.PublisherName,
            b.PublisherOrgId == reader,
            b.Kind,
            b.Severity,
            b.Title,
            b.Body,
            b.EntityKind,
            b.DisplayName,
            b.Aliases,
            JsonSerializer.Deserialize<Dictionary<string, string>>(b.DescriptorsJson) ?? [],
            b.Areas,
            b.PublishedAt,
            b.ExpiresAt,
            b.WithdrawnAt,
            b.WithdrawnAt == null && b.ExpiresAt > now
        );
}
