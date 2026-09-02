using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Billing;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Entitlements;

/// <summary>
/// One row per org: the provider's subscription truth, mirrored (ADR 39).
/// Deletion tier 1: lifecycle status, purged with the org.
/// </summary>
public sealed class OrgSubscription : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string PlanId { get; set; }
    public required string Status { get; set; }
    public string? CustomerRef { get; set; }
    public string? SubscriptionRef { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Envelope-tenanted: the org rides the envelope, never the payload (ADR 24).</summary>
public sealed record BillingSubscriptionChanged(
    string PlanId,
    string Status,
    string? CustomerRef,
    string? SubscriptionRef,
    DateTimeOffset? CurrentPeriodEnd
);

/// <summary>
/// Maps subscription truth onto entitlements (ADR 39). Plan values write
/// with Source "plan:{id}"; OPERATOR-sourced values always survive - custody
/// outranks commerce. PastDue keeps entitlements working (grace while the
/// provider retries payment); Canceled strips plan rows so the org falls
/// back to catalog defaults. Suspension stays a HUMAN decision.
/// </summary>
public static class BillingSubscriptionChangedHandler
{
    [Transactional]
    public static async Task Handle(
        BillingSubscriptionChanged message,
        Envelope envelope,
        ITenantContext tenant,
        EntitlementsDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"billing event arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.OrgId == org, ct);
        var previousStatus = subscription?.Status;
        if (subscription is null)
        {
            subscription = new OrgSubscription
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                PlanId = message.PlanId,
                Status = message.Status,
            };
            db.Subscriptions.Add(subscription);
        }
        subscription.PlanId = message.PlanId;
        subscription.Status = message.Status;
        subscription.CustomerRef = message.CustomerRef ?? subscription.CustomerRef;
        subscription.SubscriptionRef = message.SubscriptionRef ?? subscription.SubscriptionRef;
        subscription.CurrentPeriodEnd = message.CurrentPeriodEnd;
        subscription.UpdatedAt = DateTimeOffset.UtcNow;

        var planValues =
            message.Status is SubscriptionStatus.Canceled ? null : PlanCatalog.Find(message.PlanId);
        var existing = await db.OrgEntitlements.Where(e => e.OrgId == org).ToListAsync(ct);

        // plan rows not in the (new) plan revert to defaults; operator rows
        // are untouchable in both directions
        foreach (var row in existing.Where(e => e.Source.StartsWith("plan:")))
            if (planValues is null || !planValues.Entitlements.ContainsKey(row.Code))
                db.OrgEntitlements.Remove(row);

        if (planValues is not null)
            foreach (var (code, value) in planValues.Entitlements)
            {
                var row = existing.FirstOrDefault(e => e.Code == code);
                if (row is { Source: "operator" })
                    continue; // custody outranks commerce
                if (row is null)
                    db.OrgEntitlements.Add(
                        new OrgEntitlement
                        {
                            Id = Guid.CreateVersion7(),
                            OrgId = org,
                            Code = code,
                            Value = value,
                            Source = $"plan:{planValues.Id}",
                        }
                    );
                else
                {
                    row.Value = value;
                    row.Source = $"plan:{planValues.Id}";
                    row.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

        await db.SaveChangesAsync(ct);

        // dunning (operability item 4): status TRANSITIONS notify the people
        // who can fix them - repeat webhooks for the same state stay quiet
        if (previousStatus != message.Status && message.Status == SubscriptionStatus.PastDue)
            await bus.PublishAsync(
                new SendOrgNotice(
                    "Action needed: your payment failed",
                    [
                        "The latest payment for your subscription did not go through.",
                        message.CurrentPeriodEnd is { } graceEnd
                            ? $"Your plan's features continue while we retry, but the subscription ends {graceEnd:MMMM d, yyyy} unless the payment method is fixed."
                            : "Your plan's features continue while we retry, but the subscription will end unless the payment method is fixed.",
                        "Update your card from Settings -> Billing -> Manage billing in the console.",
                    ]
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
        if (previousStatus != message.Status && message.Status == SubscriptionStatus.Canceled)
            await bus.PublishAsync(
                new SendOrgNotice(
                    "Your subscription has ended",
                    [
                        "Your subscription is canceled and your organization is back on the free tier's limits.",
                        "Your data is untouched. Resubscribe any time from Settings -> Billing.",
                    ]
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
        await bus.AuditAsync(
            org,
            AuditActor.System,
            "billing.subscription_changed",
            new { planId = message.PlanId, status = message.Status }
        );
    }
}

/// <summary>
/// Dev/test provider (ADR 39): relative checkout URL into the dev-complete
/// endpoint, no portal, shared-secret webhooks. Refused in Production by the
/// composition root, same as the local auth provider and key wrapper.
/// </summary>
public sealed class LocalBillingProvider(string webhookSecret) : IBillingProvider
{
    public string Name => "local";

    public Task<Uri> CreateCheckoutUrlAsync(
        OrgId org,
        string planId,
        string successUrl,
        string cancelUrl,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            new Uri(
                $"/billing/dev/complete?org={org.Value}&plan={Uri.EscapeDataString(planId)}&returnUrl={Uri.EscapeDataString(successUrl)}",
                UriKind.Relative
            )
        );

    public Task<Uri?> CreatePortalUrlAsync(
        string customerRef,
        string returnUrl,
        CancellationToken ct = default
    ) => Task.FromResult<Uri?>(null);

    public Task<BillingEvent?> ParseWebhookAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default
    )
    {
        if (
            !headers.TryGetValue("X-Billing-Secret", out var secret)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(secret),
                System.Text.Encoding.UTF8.GetBytes(webhookSecret)
            )
        )
            return Task.FromResult<BillingEvent?>(null);
        var payload = JsonSerializer.Deserialize<LocalPayload>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        if (payload is null || payload.OrgId == Guid.Empty || payload.PlanId is null)
            return Task.FromResult<BillingEvent?>(null);
        return Task.FromResult<BillingEvent?>(
            new BillingEvent(
                new OrgId(payload.OrgId),
                payload.PlanId,
                payload.Status ?? SubscriptionStatus.Active,
                payload.CustomerRef,
                payload.SubscriptionRef,
                payload.CurrentPeriodEnd
            )
        );
    }

    private sealed record LocalPayload(
        Guid OrgId,
        string? PlanId,
        string? Status,
        string? CustomerRef,
        string? SubscriptionRef,
        DateTimeOffset? CurrentPeriodEnd
    );
}
