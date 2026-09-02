using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 41 degradation: the local dev provider has no admin portal and no
/// directory-event source, and every surface says so instead of erroring.
/// </summary>
public class SsoEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Local_provider_reports_sso_unavailable()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var sso = await owner.GetFromJsonAsync<JsonElement>("/api/org/sso");
        Assert.False(sso.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Directory_webhook_404s_without_the_capability()
    {
        var response = await fixture
            .GuestClient()
            .PostAsync("/auth/directory/webhook", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Portal_gates_entitlement_before_provider_availability()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);

        // gate 1 first: 402 is the upsell even where the provider could never serve it
        var blocked = await owner.PostAsJsonAsync(
            "/api/org/sso/portal",
            new { intent = "sso", returnPath = "/settings" }
        );
        Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);

        // entitled but the provider has no portal: honest 404, not a 500
        var op = await fixture.OperatorClient();
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/sso.enabled",
                new { value = "true" }
            )
        ).EnsureSuccessStatusCode();
        var unavailable = await owner.PostAsJsonAsync(
            "/api/org/sso/portal",
            new { intent = "sso", returnPath = "/settings" }
        );
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
    }
}
