using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Security review: the same-site redirect guards blocked '//' but allowed
/// '/\evil.com', which browsers normalize to '//evil.com' (protocol-relative,
/// off-site). Verified through the checkout return path, which echoes the
/// guarded value; SafePath and the auth SafeReturnUrl share the rule.
/// </summary>
public class OpenRedirectTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<string> CheckoutUrlFor(string returnPath)
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var response = await owner.PostAsJsonAsync(
            "/api/billing/checkout",
            new { planId = "growth", returnPath }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("url")
            .GetString()!;
    }

    [Theory]
    [InlineData("/\\evil.com")] // backslash trick -> browsers see //evil.com
    [InlineData("//evil.com")] // protocol-relative
    [InlineData("https://evil.com")] // absolute
    [InlineData("/a/\\b")] // backslash anywhere in the path
    public async Task Malicious_return_paths_never_reach_the_redirect(string returnPath)
    {
        // the successUrl is origin-prefixed then url-encoded; the security
        // property is simply that the attacker host never appears anywhere
        var url = await CheckoutUrlFor(returnPath);
        Assert.DoesNotContain("evil.com", url);
        Assert.DoesNotContain("evil.com", Uri.UnescapeDataString(url));
    }

    [Fact]
    public async Task A_legitimate_same_site_path_survives()
    {
        var url = Uri.UnescapeDataString(await CheckoutUrlFor("/settings"));
        Assert.Contains("/settings", url);
    }
}
