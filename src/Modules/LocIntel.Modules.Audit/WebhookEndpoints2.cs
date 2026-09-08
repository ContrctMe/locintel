using System.Security.Cryptography;
using LocIntel.Contracts;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Http;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Secrets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Audit;

public sealed record CreateWebhookRequest(string Url, string[]? Events = null);

public sealed record WebhookDeliverySummary(
    string EventName,
    bool Ok,
    int? StatusCode,
    DateTimeOffset OccurredAt
);

public sealed record WebhookResponse(
    Guid Id,
    string Url,
    string[] Events,
    bool Active,
    DateTimeOffset CreatedAt,
    WebhookDeliverySummary? LastDelivery
);

public sealed record WebhookSecretResponse(Guid Id, string Secret);

public sealed record RotatedWebhookSecretResponse(
    Guid Id,
    string Secret,
    DateTimeOffset? PreviousSecretExpiresAt
);

public sealed record WebhookDeliveryResponse(
    string EventName,
    int Attempt,
    int? StatusCode,
    bool Ok,
    DateTimeOffset OccurredAt
);

/// <summary>
/// Webhook custody (ADR 40): create shows the signing secret ONCE (verify
/// with the same t=...,v1=HMAC scheme the template uses for inbound billing
/// webhooks), delete is immediate, ping sends a signed test delivery.
/// Production requires https and refuses loopback/private hosts (SSRF).
/// </summary>
public static class WebhookManagementEndpoints
{
    [Transactional(
        typeof(AuditDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/webhooks")]
    [ProducesResponseType(typeof(List<WebhookResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Allowed(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var endpoints = await db
            .WebhookEndpoints.OrderByDescending(e => e.CreatedAt)
            .Select(e => new
            {
                e.Id,
                e.Url,
                e.Events,
                e.Active,
                e.CreatedAt,
                lastDelivery = db
                    .WebhookDeliveries.Where(d => d.EndpointId == e.Id)
                    .OrderByDescending(d => d.OccurredAt)
                    .Select(d => new
                    {
                        d.EventName,
                        d.Ok,
                        d.StatusCode,
                        d.OccurredAt,
                    })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        return Results.Ok(endpoints);
    }

    [Transactional(typeof(AuditDbContext))]
    [WolverinePost("/api/webhooks")]
    [ProducesResponseType(typeof(WebhookSecretResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateWebhookRequest request,
        AuditDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IWebHostEnvironment environment,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org, UserId: var userId })
            return Results.Unauthorized();
        var gate = await Allowed(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        if (
            !PublicHttp.IsAllowedUrl(
                request.Url,
                environment.IsDevelopment() || environment.IsEnvironment("Testing")
            )
        )
            return ApiErrors.BadRequest("Webhook URL must be a public HTTPS endpoint on port 443.");
        if (
            request.Events is { Length: > 50 }
            || request.Events?.Any(e => string.IsNullOrWhiteSpace(e) || e.Length > 100) == true
        )
            return ApiErrors.BadRequest("Choose at most 50 event names of at most 100 characters.");
        if (!await db.TryTakeAsync(org.Value, ct))
            return ApiErrors.Conflict("Webhook configuration is busy.");
        if (await db.WebhookEndpoints.CountAsync(ct) >= WebhookDispatch.MaxEndpoints)
            return ApiErrors.Status(
                "Webhook endpoint limit reached.",
                StatusCodes.Status429TooManyRequests
            );

        var secret =
            "whsec_"
            + Convert
                .ToBase64String(RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Url = request.Url,
            EncryptedSecret = await EnvelopeCrypto.EncryptAsync(secret, kms, ct),
            Events = request.Events ?? [],
            CreatedBy = userId,
        };
        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);
        // the one and only time the signing secret leaves the server
        return Results.Ok(new WebhookSecretResponse(endpoint.Id, secret));
    }

    /// <summary>
    /// Zero-downtime secret rotation: deliveries sign with BOTH secrets for
    /// the overlap window (an extra v1 entry in the signature header, the
    /// Stripe convention), so the consumer swaps at their own pace and never
    /// rejects a delivery.
    /// </summary>
    [Transactional(typeof(AuditDbContext))]
    [WolverinePost("/api/webhooks/{id}/rotate-secret")]
    [ProducesResponseType(typeof(RotatedWebhookSecretResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> RotateSecret(
        Guid id,
        AuditDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var endpoint = await db.WebhookEndpoints.FirstOrDefaultAsync(
            w => w.Id == id && w.OrgId == org,
            ct
        );
        if (endpoint is null)
            return Results.NotFound();

        var secret =
            "whsec_"
            + Convert
                .ToBase64String(RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        endpoint.PreviousEncryptedSecret = endpoint.EncryptedSecret;
        endpoint.PreviousSecretExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
        endpoint.EncryptedSecret = await EnvelopeCrypto.EncryptAsync(secret, kms, ct);
        await db.SaveChangesAsync(ct);
        // the one and only time the new signing secret leaves the server
        return Results.Ok(
            new RotatedWebhookSecretResponse(endpoint.Id, secret, endpoint.PreviousSecretExpiresAt)
        );
    }

    [Transactional(typeof(AuditDbContext))]
    [WolverineDelete("/api/webhooks/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Delete(
        Guid id,
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Allowed(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var endpoint = await db.WebhookEndpoints.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (endpoint is null)
            return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM platform.capacity_reservations WHERE org_id = {endpoint.OrgId.Value} AND code = {WebhookDispatch.Code} AND batch_id = {id}",
            ct
        );
        db.WebhookEndpoints.Remove(endpoint);
        await db.WebhookDeliveries.Where(d => d.EndpointId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    [Transactional(
        typeof(AuditDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/webhooks/{id}/deliveries")]
    [ProducesResponseType(typeof(List<WebhookDeliveryResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> Deliveries(
        Guid id,
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Allowed(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var rows = await db
            .WebhookDeliveries.Where(d => d.EndpointId == id)
            .OrderByDescending(d => d.OccurredAt)
            .Take(50)
            .Select(d => new
            {
                d.EventName,
                d.Attempt,
                d.StatusCode,
                d.Ok,
                d.OccurredAt,
            })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    /// <summary>A signed test delivery, so integrators can verify plumbing before real events.</summary>
    [Transactional(typeof(AuditDbContext))]
    [WolverinePost("/api/webhooks/{id}/ping")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public static async Task<IResult> Ping(
        Guid id,
        AuditDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org })
            return Results.Unauthorized();
        var gate = await Allowed(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        if (!await db.WebhookEndpoints.AnyAsync(e => e.Id == id, ct))
            return Results.NotFound();
        if (
            !await WebhookDispatch.EnqueueAsync(
                db,
                org,
                new DeliverWebhook(
                    id,
                    Guid.CreateVersion7(),
                    "webhook.ping",
                    "{}",
                    DateTimeOffset.UtcNow,
                    Attempt: 1
                ),
                bus,
                ct
            )
        )
            return ApiErrors.Status(
                "Webhook delivery queue is full.",
                StatusCodes.Status429TooManyRequests
            );
        return Results.Accepted();
    }

    private static ValueTask<GateOutcome> Allowed(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    ) => Gate.RequireAsync(accessor, scopes, Capabilities.OrgManage, ct);
}
