using LocIntel.Platform.Billing;
using LocIntel.Platform.Kernel;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace LocIntel.Integrations.Stripe;

public sealed class StripeOptions
{
    public required string ApiKey { get; set; }
    public required string WebhookSecret { get; set; }

    /// <summary>Plan id -> Stripe price id. Every PlanCatalog plan needs one.</summary>
    public Dictionary<string, string> PriceIds { get; set; } = [];

    /// <summary>Point at stripe-mock for the adapter smoke; null = real Stripe.</summary>
    public string? ApiBase { get; set; }
}

/// <summary>
/// The built-in Stripe implementation of the billing seam (ADR 39): hosted
/// Checkout, hosted Billing Portal, signed webhooks. Org and plan ride
/// Stripe METADATA (stamped on both the checkout session and the
/// subscription), so every webhook event maps back without a lookup table.
/// </summary>
public sealed class StripeBillingProvider : IBillingProvider
{
    private const string OrgKey = "locintel_org";
    private const string PlanKey = "locintel_plan";

    private readonly StripeClient _client;
    private readonly StripeOptions _options;

    public StripeBillingProvider(IOptions<StripeOptions> options)
    {
        _options = options.Value;
        _client = new StripeClient(_options.ApiKey, apiBase: _options.ApiBase);
    }

    public string Name => "stripe";

    public async Task<Uri> CreateCheckoutUrlAsync(
        OrgId org,
        string planId,
        string successUrl,
        string cancelUrl,
        CancellationToken ct = default
    )
    {
        if (!_options.PriceIds.TryGetValue(planId, out var priceId))
            throw new InvalidOperationException(
                $"no Stripe price configured for plan '{planId}' (Billing:Stripe:PriceIds)."
            );
        var metadata = new Dictionary<string, string>
        {
            [OrgKey] = org.Value.ToString(),
            [PlanKey] = planId,
        };
        var session = await new SessionService(_client).CreateAsync(
            new SessionCreateOptions
            {
                Mode = "subscription",
                LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                ClientReferenceId = org.Value.ToString(),
                Metadata = metadata,
                SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata },
            },
            cancellationToken: ct
        );
        return new Uri(session.Url);
    }

    public async Task<Uri?> CreatePortalUrlAsync(
        string customerRef,
        string returnUrl,
        CancellationToken ct = default
    )
    {
        var session = await new global::Stripe.BillingPortal.SessionService(_client).CreateAsync(
            new global::Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerRef,
                ReturnUrl = returnUrl,
            },
            cancellationToken: ct
        );
        return new Uri(session.Url);
    }

    public Task<BillingEvent?> ParseWebhookAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default
    )
    {
        var signature = headers.GetValueOrDefault("Stripe-Signature");
        if (string.IsNullOrEmpty(signature))
            return Task.FromResult<BillingEvent?>(null); // unsigned: drop, never parse
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                body,
                signature,
                _options.WebhookSecret,
                throwOnApiVersionMismatch: false
            );
        }
        catch (StripeException)
        {
            return Task.FromResult<BillingEvent?>(null); // unverifiable: drop
        }

        BillingEvent? billingEvent = stripeEvent.Type switch
        {
            "checkout.session.completed" when stripeEvent.Data.Object is Session session => Map(
                session.Metadata,
                SubscriptionStatus.Active,
                session.CustomerId,
                session.SubscriptionId,
                null
            ),
            "customer.subscription.updated"
            or "customer.subscription.deleted"
                when stripeEvent.Data.Object is Subscription subscription => Map(
                subscription.Metadata,
                stripeEvent.Type == "customer.subscription.deleted"
                    ? SubscriptionStatus.Canceled
                    : MapStatus(subscription.Status),
                subscription.CustomerId,
                subscription.Id,
                subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd
            ),
            _ => null, // irrelevant event types are fine to ignore
        };
        return Task.FromResult<BillingEvent?>(billingEvent);
    }

    private static BillingEvent? Map(
        IDictionary<string, string>? metadata,
        string status,
        string? customerRef,
        string? subscriptionRef,
        DateTime? periodEnd
    )
    {
        if (
            metadata is null
            || !metadata.TryGetValue(OrgKey, out var orgRaw)
            || !metadata.TryGetValue(PlanKey, out var planId)
            || !Guid.TryParse(orgRaw, out var orgId)
        )
            return null; // not one of ours
        return new BillingEvent(
            new OrgId(orgId),
            planId,
            status,
            customerRef,
            subscriptionRef,
            periodEnd is { } end ? new DateTimeOffset(end, TimeSpan.Zero) : null
        );
    }

    private static string MapStatus(string stripeStatus) =>
        stripeStatus switch
        {
            "active" => SubscriptionStatus.Active,
            "trialing" => SubscriptionStatus.Trialing,
            "past_due" or "unpaid" => SubscriptionStatus.PastDue,
            "canceled" or "incomplete_expired" => SubscriptionStatus.Canceled,
            _ => SubscriptionStatus.PastDue, // fail toward grace, never toward loss
        };
}
