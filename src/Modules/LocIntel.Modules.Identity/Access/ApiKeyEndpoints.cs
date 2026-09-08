using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Access;

public sealed record CreateApiKeyRequest(
    string Name,
    Guid RoleId,
    string? ScopePath = null,
    int? ExpiresInDays = null
);

public sealed record RotateApiKeyRequest(int? OverlapHours = null);

public sealed record ApiKeyResponse(
    Guid Id,
    string Name,
    string Prefix,
    string Role,
    string? ScopePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    bool Revoked
);

public sealed record ApiKeySecretResponse(Guid Id, string Secret, string Prefix);

public sealed record RotatedApiKeyResponse(
    Guid Id,
    string Secret,
    string Prefix,
    DateTimeOffset? OldKeyExpiresAt
);

/// <summary>
/// API-key custody (ADR 40): create shows the secret ONCE, the list shows
/// prefixes, revocation is immediate (the resolver consults the row per
/// request). org:manage gated - a key can carry any role, so minting one is
/// an org-level power.
/// </summary>
public static class ApiKeyEndpoints
{
    [Transactional(
        typeof(IdentityDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/api-keys")]
    [ProducesResponseType(typeof(List<ApiKeyResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var keys = await (
            from key in db.ApiKeys
            where key.OrgId == org
            join role in db.Roles on key.RoleId equals role.Id
            orderby key.CreatedAt descending
            select new
            {
                key.Id,
                key.Name,
                key.Prefix,
                role = role.Name,
                key.ScopePath,
                key.CreatedAt,
                key.LastUsedAt,
                key.ExpiresAt,
                revoked = key.RevokedAt != null,
            }
        ).ToListAsync(ct);
        return Results.Ok(keys);
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/api/api-keys")]
    [ProducesResponseType(typeof(ApiKeySecretResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateApiKeyRequest request,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
            return ApiErrors.BadRequest("name must be 1-120 characters");
        if (!await db.Roles.AnyAsync(r => r.Id == request.RoleId && r.OrgId == org, ct))
            return ApiErrors.NotFound("unknown role");
        if (request.ExpiresInDays is < 1 or > 3650)
            return ApiErrors.BadRequest("expiresInDays must be 1-3650");

        var secret =
            "locintel_"
            + Convert
                .ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Name = request.Name.Trim(),
            SecretHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
            Prefix = secret[..16],
            RoleId = request.RoleId,
            ScopePath = request.ScopePath,
            CreatedBy = userId,
            ExpiresAt = request.ExpiresInDays is { } days
                ? DateTimeOffset.UtcNow.AddDays(days)
                : null,
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        await PublishAudit(bus, org, userId, "apikey.created", key.Id, key.Name);
        // the one and only time the secret leaves the server
        return Results.Ok(new ApiKeySecretResponse(key.Id, secret, key.Prefix));
    }

    /// <summary>
    /// Zero-downtime rotation: a NEW key (same name, role, scope) is minted
    /// and the old one gets an overlap window instead of dying instantly -
    /// swap the consumer at leisure, the old credential retires itself.
    /// </summary>
    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/api/api-keys/{id}/rotate")]
    [ProducesResponseType(typeof(RotatedApiKeyResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Rotate(
        Guid id,
        RotateApiKeyRequest request,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        if (request.OverlapHours is < 0 or > 168)
            return ApiErrors.BadRequest("overlapHours must be 0-168");
        var old = await db.ApiKeys.FirstOrDefaultAsync(
            k => k.Id == id && k.OrgId == org && k.RevokedAt == null,
            ct
        );
        if (old is null)
            return Results.NotFound();

        var secret =
            "locintel_"
            + Convert
                .ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        var replacement = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Name = old.Name,
            SecretHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
            Prefix = secret[..16],
            RoleId = old.RoleId,
            ScopePath = old.ScopePath,
            CreatedBy = userId,
            ExpiresAt = old.ExpiresAt is { } lifetime
                ? DateTimeOffset.UtcNow + (lifetime - old.CreatedAt) // same lifetime as the original
                : null,
        };
        db.ApiKeys.Add(replacement);
        var overlap = DateTimeOffset.UtcNow.AddHours(request.OverlapHours ?? 24);
        old.ExpiresAt = old.ExpiresAt is { } existing && existing < overlap ? existing : overlap;
        await db.SaveChangesAsync(ct);
        await PublishAudit(bus, org, userId, "apikey.rotated", replacement.Id, replacement.Name);
        // the one and only time the new secret leaves the server
        return Results.Ok(
            new RotatedApiKeyResponse(replacement.Id, secret, replacement.Prefix, old.ExpiresAt)
        );
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineDelete("/api/api-keys/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Revoke(
        Guid id,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        var key = await db.ApiKeys.FirstOrDefaultAsync(
            k => k.Id == id && k.OrgId == org && k.RevokedAt == null,
            ct
        );
        if (key is null)
            return Results.NotFound();
        key.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await PublishAudit(bus, org, userId, "apikey.revoked", key.Id, key.Name);
        return Results.NoContent();
    }

    private static Task PublishAudit(
        IMessageBus bus,
        OrgId org,
        Guid actorId,
        string eventName,
        Guid keyId,
        string name
    ) => bus.AuditAsync(org, AuditActor.User(actorId), eventName, new { keyId, name }).AsTask();
}
