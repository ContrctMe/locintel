using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The billing seam (ADR 39): the provider's webhook is the only writer of
/// subscription truth, plans map onto entitlements, and operator custody
/// outranks commerce in both directions.
/// </summary>
public class BillingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static StringContent WebhookPayload(Guid orgId, string planId, string status) =>
        new(
            JsonSerializer.Serialize(
                new
                {
                    orgId,
                    planId,
                    status,
                }
            ),
            Encoding.UTF8,
            "application/json"
        );

    private async Task<string?> EffectiveValue(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/entitlements"))
            .GetProperty(code)
            .GetProperty("value")
            .GetString();

    private async Task PostWebhook(Guid orgId, string planId, string status)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/billing/webhook")
        {
            Content = WebhookPayload(orgId, planId, status),
        };
        request.Headers.Add("X-Billing-Secret", "dev-billing-secret");
        var response = await fixture.GuestClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task WaitForPlan(HttpClient client, string? planId)
    {
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                var billing = await client.GetFromJsonAsync<JsonElement>("/api/billing");
                return (
                        billing.GetProperty("planId").ValueKind == JsonValueKind.Null
                            ? null
                            : billing.GetProperty("planId").GetString()
                    ) == planId;
            },
            $"the subscription to reach plan {planId}"
        );
    }

    [Fact]
    public async Task Free_tier_is_the_default_and_plans_are_listed()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserB); // org B: isolated from the upgrade test
        var billing = await owner.GetFromJsonAsync<JsonElement>("/api/billing");
        Assert.Equal("Free", billing.GetProperty("planName").GetString());
        Assert.Equal(JsonValueKind.Null, billing.GetProperty("status").ValueKind);
        Assert.False(billing.GetProperty("portalAvailable").GetBoolean());
        Assert.Equal(2, billing.GetProperty("plans").GetArrayLength());
    }

    [Fact]
    public async Task Checkout_returns_the_provider_url_and_rejects_unknown_plans()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserB);
        var checkout = await owner.PostAsJsonAsync(
            "/api/billing/checkout",
            new { planId = "growth", returnPath = "/settings" }
        );
        checkout.EnsureSuccessStatusCode();
        var url = (await checkout.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("url")
            .GetString();
        Assert.Contains("/billing/dev/complete", url);
        Assert.Contains("plan=growth", url);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PostAsJsonAsync(
                    "/api/billing/checkout",
                    new { planId = "imaginary", returnPath = "/" }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task Unverifiable_webhooks_are_dropped()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/billing/webhook")
        {
            Content = WebhookPayload(fixture.OrgA.Value, "growth", "Active"),
        };
        request.Headers.Add("X-Billing-Secret", "wrong");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await fixture.GuestClient().SendAsync(request)).StatusCode
        );
    }

    [Fact]
    public async Task Subscription_lifecycle_maps_plans_onto_entitlements_and_custody_survives()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var op = await fixture.OperatorClient();

        // the operator holds custody of one value BEFORE any plan exists
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/sites.max",
                new { value = "3" }
            )
        ).EnsureSuccessStatusCode();

        // upgrade: the webhook is the writer
        await PostWebhook(fixture.OrgA.Value, "growth", "Active");
        await WaitForPlan(owner, "growth");

        var billing = await owner.GetFromJsonAsync<JsonElement>("/api/billing");
        Assert.Equal("Growth", billing.GetProperty("planName").GetString());
        Assert.Equal("Active", billing.GetProperty("status").GetString());

        // plan values applied - except the operator-held one (custody wins)
        Assert.Equal("6", await EffectiveValue(owner, "hierarchy.depth"));
        Assert.Equal("10000", await EffectiveValue(owner, "contact_links.monthly"));
        Assert.Equal("5000", await EffectiveValue(owner, "reports.monthly"));
        Assert.Equal("3", await EffectiveValue(owner, "sites.max"));

        // plan change reshapes the plan rows
        await PostWebhook(fixture.OrgA.Value, "scale", "Active");
        await WaitForPlan(owner, "scale");
        Assert.Equal("8", await EffectiveValue(owner, "hierarchy.depth"));
        Assert.Equal("50000", await EffectiveValue(owner, "reports.monthly"));
        Assert.Equal("3", await EffectiveValue(owner, "sites.max"));

        // cancellation strips plan rows back to defaults; custody still holds
        await PostWebhook(fixture.OrgA.Value, "scale", "Canceled");
        await ApiFixture.WaitUntilAsync(
            async () => await EffectiveValue(owner, "hierarchy.depth") == "4",
            "entitlements to fall back to the catalog default after cancellation"
        );
        Assert.Equal("4", await EffectiveValue(owner, "hierarchy.depth")); // catalog default
        Assert.Equal("1000", await EffectiveValue(owner, "reports.monthly"));
        Assert.Equal("3", await EffectiveValue(owner, "sites.max")); // operator row survives
        var canceled = await owner.GetFromJsonAsync<JsonElement>("/api/billing");
        Assert.Equal("Canceled", canceled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Billing_is_org_manage_gated()
    {
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/billing")).StatusCode);
    }

    [Fact]
    public async Task Past_due_transition_emails_the_org_managers_once()
    {
        var catcher =
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<LocIntel.Platform.Notifications.LocalMailCatcher>(
                fixture.Factory.Services
            );
        // a FRESH org: OrgA and OrgB belong to the other billing tests
        var owner = await fixture.LoginAsync("dunning-owner@locintel.local");
        var created = await owner.PostAsJsonAsync(
            "/api/orgs",
            new { name = "Dunning Co", slug = "dunning-co" }
        );
        created.EnsureSuccessStatusCode();
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("orgId")
            .GetGuid();
        await ApiFixture.WaitForMembershipAsync(owner);
        (await owner.PostAsJsonAsync("/auth/switch-org", new { orgId })).EnsureSuccessStatusCode();

        await PostWebhook(orgId, "growth", "Active");
        await WaitForPlan(owner, "growth");
        await PostWebhook(orgId, "growth", "PastDue");

        LocIntel.Platform.Notifications.EmailMessage? mail = null;
        for (var i = 0; i < 200 && mail is null; i++)
        {
            mail = catcher.Sent.FirstOrDefault(m =>
                m.To == "dunning-owner@locintel.local" && m.Subject.Contains("payment failed")
            );
            if (mail is null)
                await Task.Delay(100);
        }
        Assert.NotNull(mail);
        Assert.Contains("Manage billing", mail!.TextBody);

        // a REPEAT webhook for the same state stays quiet; only transitions speak
        await PostWebhook(orgId, "growth", "PastDue");
        // and the console has the fact to render the warning from
        var billing = await owner.GetFromJsonAsync<JsonElement>("/api/billing");
        Assert.Equal("PastDue", billing.GetProperty("status").GetString());
        await Task.Delay(500);
        Assert.Equal(
            1,
            catcher.Sent.Count(m =>
                m.To == "dunning-owner@locintel.local" && m.Subject.Contains("payment failed")
            )
        );
    }
}
