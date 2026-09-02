using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using LocIntel.Integrations.Stripe;
using LocIntel.Platform.Billing;
using LocIntel.Platform.Kernel;
using Microsoft.Extensions.Options;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Smokes the REAL Stripe adapter: hosted-session creation against
/// stripe-mock (the same client wiring production uses), and webhook
/// signature verification with hand-signed payloads - no mocks of our own
/// code anywhere (ADR 39).
/// </summary>
public sealed class StripeMockFixture : IAsyncLifetime
{
    private readonly IContainer _mock = new ContainerBuilder("stripe/stripe-mock:latest")
        .WithPortBinding(12111, assignRandomHostPort: true)
        // stripe-mock 404s on "/": accept any HTTP response as readiness
        .WithWaitStrategy(
            Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r =>
                    r.ForPath("/").ForPort(12111).ForStatusCodeMatching(_ => true)
                )
        )
        .Build();

    public const string WebhookSecret = "whsec_smoke";
    public StripeBillingProvider Provider { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _mock.StartAsync();
        Provider = new StripeBillingProvider(
            Options.Create(
                new StripeOptions
                {
                    ApiKey = "sk_test_smoke",
                    WebhookSecret = WebhookSecret,
                    PriceIds = { ["growth"] = "price_growth", ["scale"] = "price_scale" },
                    ApiBase = $"http://{_mock.Hostname}:{_mock.GetMappedPublicPort(12111)}",
                }
            )
        );
    }

    public async Task DisposeAsync() => await _mock.DisposeAsync();
}

public class StripeAdapterTests(StripeMockFixture fixture) : IClassFixture<StripeMockFixture>
{
    private static string Sign(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signed = $"{timestamp}.{payload}";
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signed))
        );
        return $"t={timestamp},v1={signature}";
    }

    [Fact]
    public async Task Hosted_sessions_create_through_the_real_client()
    {
        var checkout = await fixture.Provider.CreateCheckoutUrlAsync(
            OrgId.New(),
            "growth",
            "https://console.example/settings",
            "https://console.example/settings"
        );
        Assert.StartsWith("https://", checkout.ToString());

        var portal = await fixture.Provider.CreatePortalUrlAsync(
            "cus_smoke",
            "https://console.example/settings"
        );
        Assert.NotNull(portal);
        Assert.StartsWith("https://", portal!.ToString());
    }

    [Fact]
    public async Task Unpriced_plans_refuse_loudly()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Provider.CreateCheckoutUrlAsync(
                OrgId.New(),
                "imaginary",
                "https://x",
                "https://x"
            )
        );
    }

    [Fact]
    public async Task Signed_checkout_webhook_maps_to_a_billing_event()
    {
        var org = Guid.CreateVersion7();
        var payload = $$"""
            {
              "id": "evt_smoke",
              "object": "event",
              "api_version": "2025-01-01",
              "type": "checkout.session.completed",
              "data": {
                "object": {
                  "id": "cs_smoke",
                  "object": "checkout.session",
                  "customer": "cus_abc",
                  "subscription": "sub_abc",
                  "metadata": { "locintel_org": "{{org}}", "locintel_plan": "growth" }
                }
              }
            }
            """;
        var billingEvent = await fixture.Provider.ParseWebhookAsync(
            payload,
            new Dictionary<string, string>
            {
                ["Stripe-Signature"] = Sign(payload, StripeMockFixture.WebhookSecret),
            }
        );
        Assert.NotNull(billingEvent);
        Assert.Equal(org, billingEvent!.OrgId.Value);
        Assert.Equal("growth", billingEvent.PlanId);
        Assert.Equal(SubscriptionStatus.Active, billingEvent.Status);
        Assert.Equal("cus_abc", billingEvent.CustomerRef);

        // tampered or missing signature: dropped, never trusted
        Assert.Null(
            await fixture.Provider.ParseWebhookAsync(
                payload,
                new Dictionary<string, string>
                {
                    ["Stripe-Signature"] = Sign(payload, "whsec_wrong"),
                }
            )
        );
        Assert.Null(
            await fixture.Provider.ParseWebhookAsync(payload, new Dictionary<string, string>())
        );
    }

    [Fact]
    public async Task Subscription_deletion_maps_to_canceled_and_foreign_events_are_ignored()
    {
        var org = Guid.CreateVersion7();
        var deleted = $$"""
            {
              "id": "evt_del",
              "object": "event",
              "api_version": "2025-01-01",
              "type": "customer.subscription.deleted",
              "data": {
                "object": {
                  "id": "sub_abc",
                  "object": "subscription",
                  "customer": "cus_abc",
                  "status": "canceled",
                  "metadata": { "locintel_org": "{{org}}", "locintel_plan": "growth" }
                }
              }
            }
            """;
        var billingEvent = await fixture.Provider.ParseWebhookAsync(
            deleted,
            new Dictionary<string, string>
            {
                ["Stripe-Signature"] = Sign(deleted, StripeMockFixture.WebhookSecret),
            }
        );
        Assert.Equal(SubscriptionStatus.Canceled, billingEvent!.Status);

        // a Stripe event without OUR metadata is someone else's business
        var foreign = deleted.Replace("locintel_org", "someone_elses_key");
        Assert.Null(
            await fixture.Provider.ParseWebhookAsync(
                foreign,
                new Dictionary<string, string>
                {
                    ["Stripe-Signature"] = Sign(foreign, StripeMockFixture.WebhookSecret),
                }
            )
        );
    }
}
