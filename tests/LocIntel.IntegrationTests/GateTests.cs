using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The three gates' status contract, proven ONCE at the seam (Gate +
/// GateResults) rather than re-proven per endpoint. Per-endpoint tests still
/// assert which capability each endpoint demands; this class asserts what a
/// failure looks like.
/// </summary>
public class GateTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // a user-only surface (roles:manage) and an operator surface
    private const string UserOnly = "/api/members";
    private const string OperatorOnly = "/api/operator/overview";

    [Fact]
    public async Task No_principal_is_401_not_403()
    {
        // a guest cannot be asked "do you hold the grant" - sign in first
        var guest = fixture.GuestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(UserOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(OperatorOnly)).StatusCode);
    }

    [Fact]
    public async Task Signed_in_without_the_grant_is_403_and_names_the_capability()
    {
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        var denied = await viewer.GetAsync(UserOnly);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // the one 403 body: which grant was missing, so a client can explain
        var body = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("missing grant", body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("capability").GetString()));
    }

    [Fact]
    public async Task An_org_owner_is_403_at_the_operator_wall()
    {
        // *:* in your own org never crosses the platform line (gate 2's
        // platform edition) - and it is a grant failure, so 403, not 401
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(OperatorOnly)).StatusCode);
    }

    [Fact]
    public async Task An_api_key_on_a_user_only_surface_is_401_by_design()
    {
        // service principals are first-class on org data (ADR 40) but a
        // human-only surface has no user to authorize: NotSignedIn, documented
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        var roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await owner.PostAsJsonAsync("/api/api-keys", new { name = "gate", roleId });
        created.EnsureSuccessStatusCode();
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("secret")
            .GetString()!;

        var service = fixture.Factory.CreateDefaultClient();
        service.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await service.GetAsync(UserOnly)).StatusCode);
        // ...while the same key is a full principal on org data
        (await service.GetAsync("/api/sites?limit=1")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Scope_filters_and_never_errors()
    {
        // gate 3 is not a refusal: a role-less member reads an empty list,
        // and only a WRITE meets the grant gate
        // CreateMemberAsync creates the user too - a role-less membership
        await fixture.CreateMemberAsync("gate-norole@locintel.local", fixture.OrgA);
        var member = await fixture.LoginAsync("gate-norole@locintel.local");
        Assert.Empty((await ApiFixture.GetItemsAsync(member, "/api/sites")).EnumerateArray());
    }
}
