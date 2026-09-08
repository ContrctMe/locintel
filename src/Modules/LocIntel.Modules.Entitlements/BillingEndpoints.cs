using LocIntel.Contracts;
using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Billing;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Entitlements;

public sealed record PlanSummary(
    string Id,
    string Name,
    decimal MonthlyPriceUsd,
    IReadOnlyDictionary<string, string> Entitlements
);

public sealed record BillingResponse(
    string Provider,
    string? PlanId,
    string PlanName,
    string? Status,
    DateTimeOffset? CurrentPeriodEnd,
    bool PortalAvailable,
    IReadOnlyList<PlanSummary> Plans
);

public sealed record CheckoutRequest(string PlanId, string ReturnPath);

public sealed record PortalRequest(string ReturnPath);

public sealed record BillingLinkResponse(string Url);

/// <summary>
/// The tenant's side of the billing seam (ADR 39): see the plan, buy a plan,
/// manage it - all through provider-hosted URLs. The webhook below is the
/// only ingestion point for subscription truth.
/// </summary>
public static class BillingEndpoints
{
    [Transactional(
        typeof(EntitlementsDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/billing")]
    [ProducesResponseType(typeof(BillingResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        EntitlementsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IBillingProvider provider,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var subscription = await db
            .Subscriptions.Where(s => s.OrgId == org)
            .Select(s => new
            {
                s.PlanId,
                s.Status,
                s.CurrentPeriodEnd,
                hasCustomer = s.CustomerRef != null,
            })
            .FirstOrDefaultAsync(ct);
        return Results.Ok(
            new BillingResponse(
                provider.Name,
                // no subscription row = the free tier: catalog defaults
                subscription?.PlanId,
                subscription is null
                    ? "Free"
                    : (PlanCatalog.Find(subscription.PlanId)?.Name ?? subscription.PlanId),
                subscription?.Status.ToString(),
                subscription?.CurrentPeriodEnd,
                subscription?.hasCustomer == true,
                PlanCatalog
                    .Plans.Select(p => new PlanSummary(
                        p.Id,
                        p.Name,
                        p.MonthlyPriceUsd,
                        p.Entitlements
                    ))
                    .ToList()
            )
        );
    }

    [WolverinePost("/api/billing/checkout")]
    [ProducesResponseType(typeof(BillingLinkResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Checkout(
        CheckoutRequest request,
        HttpContext http,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IBillingProvider provider,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        if (PlanCatalog.Find(request.PlanId) is null)
            return ApiErrors.BadRequest("unknown plan");
        var returnPath = SafePath(request.ReturnPath);
        var origin = $"{http.Request.Scheme}://{http.Request.Host}";
        var url = await provider.CreateCheckoutUrlAsync(
            org,
            request.PlanId,
            $"{origin}{returnPath}",
            $"{origin}{returnPath}",
            ct
        );
        return Results.Ok(new BillingLinkResponse(url.ToString()));
    }

    [Transactional(typeof(EntitlementsDbContext))]
    [WolverinePost("/api/billing/portal")]
    [ProducesResponseType(typeof(BillingLinkResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Portal(
        PortalRequest request,
        HttpContext http,
        EntitlementsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IBillingProvider provider,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var customerRef = await db
            .Subscriptions.Where(s => s.OrgId == org)
            .Select(s => s.CustomerRef)
            .FirstOrDefaultAsync(ct);
        if (customerRef is null)
            return ApiErrors.NotFound("no billing account yet");
        var origin = $"{http.Request.Scheme}://{http.Request.Host}";
        var url = await provider.CreatePortalUrlAsync(
            customerRef,
            $"{origin}{SafePath(request.ReturnPath)}",
            ct
        );
        return url is null
            ? ApiErrors.NotFound("this provider has no billing portal")
            : Results.Ok(new BillingLinkResponse(url.ToString()));
    }

    /// <summary>
    /// The provider's webhook: anonymous by nature, authenticated by the
    /// provider's own signature scheme inside ParseWebhookAsync. Unverifiable
    /// or irrelevant deliveries 400 without detail.
    /// </summary>
    [WolverinePost("/billing/webhook")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public static async Task<IResult> Webhook(
        HttpContext http,
        IBillingProvider provider,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        using var reader = new StreamReader(http.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var headers = http.Request.Headers.ToDictionary(
            h => h.Key,
            h => h.Value.ToString(),
            StringComparer.OrdinalIgnoreCase
        );
        var billingEvent = await provider.ParseWebhookAsync(body, headers, ct);
        if (billingEvent is null)
            return Results.BadRequest();
        await bus.PublishAsync(
            new BillingSubscriptionChanged(
                billingEvent.PlanId,
                billingEvent.Status,
                billingEvent.CustomerRef,
                billingEvent.SubscriptionRef,
                billingEvent.CurrentPeriodEnd
            ),
            new DeliveryOptions { TenantId = billingEvent.OrgId.Value.ToString() }
        );
        return Results.Accepted();
    }

    /// <summary>Open-redirect guard: same-site path only (backslash-safe, matches SafeReturnUrl).</summary>
    private static string SafePath(string? path) =>
        path is ['/', ..] && !path.StartsWith("//") && !path.Contains('\\') ? path : "/";
}
