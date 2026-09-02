using System.Net;
using System.Net.Http.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// CSRF defence-in-depth (security review): an unsafe cookie-authenticated
/// request with a foreign Origin is refused; same-origin and Origin-less
/// (native / API-key) requests pass.
/// </summary>
public class CsrfOriginTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Cross_origin_state_change_with_the_session_cookie_is_refused()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);

        // no Origin header (the fixture client) → passes: native-client shape
        (
            await owner.PutAsJsonAsync("/api/org", new { name = "No Origin OK" })
        ).EnsureSuccessStatusCode();

        // an attacker's forged cross-site POST carries the cookie (simulated)
        // but a foreign Origin → refused before the handler runs
        var forged = new HttpRequestMessage(HttpMethod.Put, "/api/org")
        {
            Content = JsonContent.Create(new { name = "Evil" }),
        };
        forged.Headers.Add("Origin", "https://evil.example.com");
        var response = await owner.SendAsync(forged);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // same-origin Origin → passes
        var same = new HttpRequestMessage(HttpMethod.Put, "/api/org")
        {
            Content = JsonContent.Create(new { name = "Same Origin OK" }),
        };
        same.Headers.Add("Origin", "http://localhost");
        var ok = await owner.SendAsync(same);
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cookieless_callers_are_never_blocked_by_the_origin_check()
    {
        // an API key carries no session cookie, so even a foreign Origin
        // passes the CSRF layer (its own 401/scoping applies instead)
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var roles = await owner.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/roles");
        var roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await owner.PostAsJsonAsync("/api/api-keys", new { name = "csrf", roleId });
        var secret = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("secret")
            .GetString()!;

        var service = fixture.Factory.CreateDefaultClient();
        service.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/sites?limit=1");
        req.Headers.Add("Origin", "https://anywhere.example.com");
        (await service.SendAsync(req)).EnsureSuccessStatusCode();
    }
}
