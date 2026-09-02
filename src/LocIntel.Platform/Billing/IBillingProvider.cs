using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.Billing;

/// <summary>
/// The billing seam (ADR 39), shaped like the auth seam (ADR 14): hosted
/// provider UI for everything that touches money - checkout and the billing
/// portal are provider-hosted URLs, so card data never crosses the template.
/// The provider's webhook is the ONLY writer of subscription truth; the
/// template maps it onto entitlements.
/// </summary>
public interface IBillingProvider
{
    string Name { get; }

    /// <summary>Hosted checkout for a plan. The URL may be relative (local provider) or absolute (Stripe).</summary>
    Task<Uri> CreateCheckoutUrlAsync(
        OrgId org,
        string planId,
        string successUrl,
        string cancelUrl,
        CancellationToken ct = default
    );

    /// <summary>Hosted billing portal (cards, invoices, cancellation). Null = the provider has none.</summary>
    Task<Uri?> CreatePortalUrlAsync(
        string customerRef,
        string returnUrl,
        CancellationToken ct = default
    );

    /// <summary>
    /// Validate and translate a webhook delivery. Framework-neutral on
    /// purpose: raw body + headers in, a billing event (or null for
    /// deliveries that are unverifiable or irrelevant) out.
    /// </summary>
    Task<BillingEvent?> ParseWebhookAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default
    );
}

/// <summary>What every provider's webhook boils down to.</summary>
public sealed record BillingEvent(
    OrgId OrgId,
    string PlanId,
    string Status,
    string? CustomerRef,
    string? SubscriptionRef,
    DateTimeOffset? CurrentPeriodEnd
);

public static class SubscriptionStatus
{
    public const string Active = "Active";
    public const string Trialing = "Trialing";

    /// <summary>Payment failed, provider retrying: entitlements KEEP working (grace).</summary>
    public const string PastDue = "PastDue";

    /// <summary>Plan entitlements are stripped; the org falls back to catalog defaults.</summary>
    public const string Canceled = "Canceled";
}
