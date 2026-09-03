using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Contracts.Entities;
using LocIntel.Modules.Network.Bulletins.Api;
using LocIntel.Modules.Network.Bulletins.Messages;
using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Network.Bulletins;

/// <summary>
/// What flows through a share (ADR 48): a publisher owns what it published;
/// every active member owns a COPY, materialized through the outbox and
/// withdrawn the same way. A member reads its copies, imports one as its
/// own record, and withdraws only what it published.
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkRead, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var now = time.GetUtcNow();
        var mine = db.Bulletins.AsQueryable();
        var copies = db.BulletinCopies.AsQueryable();
        if (shareId is { } share)
        {
            mine = mine.Where(b => b.ShareId == share);
            copies = copies.Where(c => c.ShareId == share);
        }
        if (includeInactive is not true)
        {
            mine = mine.Where(b => b.WithdrawnAt == null && b.ExpiresAt > now);
            copies = copies.Where(c => c.WithdrawnAt == null && c.ExpiresAt > now);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            mine = mine.Where(b =>
                EF.Functions.ILike(b.Title, needle)
                || EF.Functions.ILike(b.Body, needle)
                || (b.DisplayName != null && EF.Functions.ILike(b.DisplayName, needle))
                || b.Aliases.Any(a => EF.Functions.ILike(a, needle))
            );
            copies = copies.Where(c =>
                EF.Functions.ILike(c.Title, needle)
                || EF.Functions.ILike(c.Body, needle)
                || (c.DisplayName != null && EF.Functions.ILike(c.DisplayName, needle))
                || c.Aliases.Any(a => EF.Functions.ILike(a, needle))
            );
        }
        var shareNames = await db.Access.ToDictionaryAsync(a => a.ShareId, a => a.ShareName, ct);
        var all = (await mine.ToListAsync(ct))
            .Select(b => View(b, shareNames.GetValueOrDefault(b.ShareId, ""), now))
            .Concat((await copies.ToListAsync(ct)).Select(c => View(c, now)))
            .OrderByDescending(v => v.PublishedAt)
            .ThenByDescending(v => v.Id)
            .ToList();
        var total = all.Count;
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var items = all.Skip(skip).Take(take).ToList();
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        if (await ShareEndpoints.Upsell(entitlements, actor.Org, ct) is { } upsell)
            return upsell;
        var access = await ShareEndpoints.LoadAsync(id, db, ct);
        if (access is null)
            return Results.NotFound();
        if (access.Status != MembershipStatus.Active)
            return Results.Conflict(new { error = "accept the invitation before publishing" });
        if (access.ShareStatus == ShareStatus.Closed)
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
            access.Roster().FirstOrDefault(m => m.OrgId == actor.Org.Value)?.OrgName ?? "Member";
        var bulletin = new SharedBulletin
        {
            Id = Guid.CreateVersion7(),
            ShareId = id,
            OrgId = actor.Org,
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
        var peers = ShareRoster.ActivePeers(access).ToList();
        await bus.FanOutAsync(peers, bulletin.Offered(access.ShareName), bulletin.Id);
        foreach (var org in peers)
            await bus.PublishForOrgAsync(
                org,
                new SendOrgNotice(
                    $"Shared {bulletin.Kind.ToString().ToUpperInvariant()} in {access.ShareName}: {bulletin.Title}",
                    [
                        $"From {publisher}.",
                        bulletin.Body,
                        "Open Network in the console to review or import it.",
                    ],
                    "network",
                    $"Shared {bulletin.Kind.ToString().ToUpperInvariant()} in {access.ShareName}: {bulletin.Title}"
                )
            );
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var bulletin = await db.Bulletins.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bulletin is null)
            return Results.NotFound();
        if (bulletin.WithdrawnAt is null)
        {
            bulletin.WithdrawnAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            var access = await ShareEndpoints.LoadAsync(bulletin.ShareId, db, ct);
            if (access is not null)
                await bus.FanOutAsync(
                    ShareRoster.ActivePeers(access),
                    new SharedBulletinWithdrawn(bulletin.Id, bulletin.WithdrawnAt.Value),
                    bulletin.Id
                );
            await bus.AuditAsync(
                actor.Org,
                actor.Audit,
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.NetworkManage, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var now = time.GetUtcNow();
        if (await db.Bulletins.AnyAsync(b => b.Id == id, ct))
            return Results.Conflict(new { error = "this is your own record" });
        var copy = await db.BulletinCopies.FirstOrDefaultAsync(
            c => c.BulletinId == id && c.WithdrawnAt == null && c.ExpiresAt > now,
            ct
        );
        if (copy is null)
            return Results.NotFound();
        if (copy.DisplayName is null)
            return Results.Conflict(new { error = "this bulletin carries no record to import" });
        await bus.PublishForOrgAsync(
            actor.Org,
            new ImportEntityRequested(
                copy.EntityKind ?? "Person",
                copy.DisplayName,
                copy.Aliases,
                JsonSerializer.Deserialize<Dictionary<string, string>>(copy.DescriptorsJson) ?? [],
                $"Imported from \"{copy.ShareName}\", published by {copy.PublisherName} on {copy.PublishedAt:yyyy-MM-dd}: {copy.Title}. {copy.Body}",
                actor.Id
            )
        );
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "network.bulletin_imported",
            new { BulletinId = copy.BulletinId, copy.ShareId }
        );
        return Results.Ok(new ImportQueued(copy.BulletinId));
    }

    private static SharedBulletinView View(
        SharedBulletin b,
        string shareName,
        DateTimeOffset now
    ) =>
        new(
            b.Id,
            b.ShareId,
            shareName,
            b.OrgId.Value,
            b.PublisherName,
            true,
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

    private static SharedBulletinView View(SharedBulletinCopy c, DateTimeOffset now) =>
        new(
            c.BulletinId,
            c.ShareId,
            c.ShareName,
            c.PublisherOrgId.Value,
            c.PublisherName,
            false,
            c.Kind,
            c.Severity,
            c.Title,
            c.Body,
            c.EntityKind,
            c.DisplayName,
            c.Aliases,
            JsonSerializer.Deserialize<Dictionary<string, string>>(c.DescriptorsJson) ?? [],
            c.Areas,
            c.PublishedAt,
            c.ExpiresAt,
            c.WithdrawnAt,
            c.WithdrawnAt == null && c.ExpiresAt > now
        );
}
