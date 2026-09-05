using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Api;

namespace LocIntel.IntegrationTests;

public class SessionContextTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Stale_session_preconditions_reject_reads_and_writes_before_effects()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserBoth);
        (
            await client.PostAsJsonAsync("/auth/switch-org", new { orgId = fixture.OrgA.Value })
        ).EnsureSuccessStatusCode();
        using var me = await client.GetAsync("/me");
        var original = Assert.Single(me.Headers.GetValues(SessionContextMiddleware.Header));
        client.DefaultRequestHeaders.Add(SessionContextMiddleware.Header, original);
        (await client.GetAsync("/api/roles")).EnsureSuccessStatusCode();
        (
            await client.PostAsJsonAsync("/auth/switch-org", new { orgId = fixture.OrgB.Value })
        ).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/roles")).StatusCode);
        var name = $"Stale role {Guid.NewGuid()}";
        using var write = await client.PostAsJsonAsync(
            "/api/roles",
            new { name, grants = new[] { new { domain = "sites", action = "read" } } }
        );
        Assert.Equal(HttpStatusCode.Conflict, write.StatusCode);
        Assert.Equal(
            "session_context_changed",
            (await write.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
        );
        using var refreshed = await client.GetAsync("/me");
        var current = Assert.Single(refreshed.Headers.GetValues(SessionContextMiddleware.Header));
        Assert.NotEqual(original, current);
        client.DefaultRequestHeaders.Remove(SessionContextMiddleware.Header);
        client.DefaultRequestHeaders.Add(SessionContextMiddleware.Header, current);
        var roles = await client.GetFromJsonAsync<JsonElement>("/api/roles");
        Assert.DoesNotContain(
            roles.EnumerateArray(),
            role => role.GetProperty("name").GetString() == name
        );
    }
}
