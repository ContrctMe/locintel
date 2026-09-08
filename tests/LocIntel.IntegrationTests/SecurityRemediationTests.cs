using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Identity.Access;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Identity.Users;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

public class SecurityRemediationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Guest_and_unprivileged_members_cannot_read_or_change_private_settings_or_hierarchy()
    {
        var guest = fixture.GuestClient();
        guest.DefaultRequestHeaders.Host = "org-a.example.test";
        var setting = await fixture.SettingIdOf(fixture.OrgA, "brand.color");
        foreach (var url in new[] { "/api/settings", $"/api/settings/{setting}", "/api/hierarchy" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(url)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await guest.PutAsJsonAsync("/api/settings/brand.color", new { value = "attacker" })
            ).StatusCode
        );
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/hierarchy")).StatusCode
        );
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PutAsJsonAsync("/api/settings/map.basemaps", new { value = "{}" })
            ).StatusCode
        );
    }

    [Fact]
    public async Task OAuth_callback_requires_the_initiating_browser_and_consumes_its_correlation_cookie()
    {
        var browser = fixture.Factory.CreateDefaultClient(new CookieContainerHandler());
        var login = await browser.GetAsync(
            $"/auth/login?hint={Uri.EscapeDataString(ApiFixture.UserA)}"
        );
        var callback = login.Headers.Location!;
        var stranger = fixture.Factory.CreateDefaultClient(new CookieContainerHandler());
        Assert.Equal(HttpStatusCode.BadRequest, (await stranger.GetAsync(callback)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(callback)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync(callback)).StatusCode);
        Assert.Equal(
            "guest",
            (await stranger.GetFromJsonAsync<JsonElement>("/me")).GetProperty("tier").GetString()
        );
    }

    [Fact]
    public async Task Idempotency_is_session_bound_and_never_replays_one_time_secrets()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        var roleId = roles
            .EnumerateArray()
            .First(r => r.GetProperty("name").GetString() == "Owner")
            .GetProperty("id")
            .GetGuid();
        var key = Guid.NewGuid().ToString();
        HttpRequestMessage Request() =>
            new(HttpMethod.Post, "/api/api-keys")
            {
                Headers = { { "Idempotency-Key", key } },
                Content = JsonContent.Create(new { name = "security regression", roleId }),
            };
        var created = await owner.SendAsync(Request());
        created.EnsureSuccessStatusCode();
        Assert.Contains("secret", await created.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.SendAsync(Request())).StatusCode);
        var retry = await owner.SendAsync(Request());
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.DoesNotContain("locintel_", await retry.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Removed_members_and_subtree_administrators_cannot_retain_or_expand_authority()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        await using var db = (IdentityDbContext)
            ApiFixture.CreateCatalogContext(
                LocIntel.Api.ModuleCatalog.AllWithPlatform.Single(m => m.Schema == "identity"),
                fixture.PostgresConnectionString
            );
        var email = $"security-{Guid.NewGuid():N}@example.test";
        var user = AppUser.Create("local", email, email, "Security test");
        db.Users.Add(user);
        db.Memberships.Add(Membership.Create(user.Id, fixture.OrgA));
        await db.SaveChangesAsync();
        var viewer = await fixture.LoginAsync(email);
        var userId = user.Id;
        var exception = new GrantException
        {
            Id = Guid.CreateVersion7(),
            OrgId = fixture.OrgA,
            UserId = userId,
            Domain = "roles",
            Action = "manage",
            ScopePath = "root.child",
            Reason = "security regression",
            GrantedBy = userId,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        db.GrantExceptions.Add(exception);
        await db.SaveChangesAsync();
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await viewer.PostAsJsonAsync(
                    "/api/grant-exceptions",
                    new
                    {
                        userId,
                        domain = "*",
                        action = "*",
                        scopePath = (string?)null,
                        reason = "escalate",
                        expiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    }
                )
            ).StatusCode
        );
        await db
            .GrantExceptions.IgnoreQueryFilters()
            .Where(e => e.Id == exception.Id)
            .ExecuteUpdateAsync(e =>
                e.SetProperty(x => x.Domain, "*")
                    .SetProperty(x => x.Action, "*")
                    .SetProperty(x => x.ScopePath, (string?)null)
            );
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/settings")).StatusCode);
        var retryKey = Guid.NewGuid().ToString();
        HttpRequestMessage SettingWrite() =>
            new(HttpMethod.Put, "/api/settings/security-replay")
            {
                Content = JsonContent.Create(new { value = "private-result" }),
                Headers = { { "Idempotency-Key", retryKey } },
            };
        (await viewer.SendAsync(SettingWrite())).EnsureSuccessStatusCode();
        // Delete directly to prove the resolver is fail-closed even for legacy orphaned exceptions.
        await db
            .Memberships.Where(m => m.UserId == userId && m.OrgId == fixture.OrgA)
            .ExecuteDeleteAsync();
        var deniedReplay = await viewer.SendAsync(SettingWrite());
        Assert.Equal(HttpStatusCode.Conflict, deniedReplay.StatusCode);
        Assert.DoesNotContain("private-result", await deniedReplay.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/settings")).StatusCode);
    }

    [Fact]
    public async Task Hierarchy_read_includes_only_authorized_subtrees_and_their_ancestors()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var root = await ApiFixture.EnsureRootAsync(owner);
        async Task<JsonElement> Child(string name)
        {
            var created = await owner.PostAsJsonAsync(
                "/api/hierarchy/nodes",
                new { parentId = root, name }
            );
            created.EnsureSuccessStatusCode();
            return await created.Content.ReadFromJsonAsync<JsonElement>();
        }
        var permitted = await Child("permitted");
        var hidden = await Child("hidden");
        await using var db = (IdentityDbContext)
            ApiFixture.CreateCatalogContext(
                LocIntel.Api.ModuleCatalog.AllWithPlatform.Single(m => m.Schema == "identity"),
                fixture.PostgresConnectionString
            );
        var email = $"hierarchy-{Guid.NewGuid():N}@example.test";
        var user = AppUser.Create("local", email, email, "Scoped reader");
        db.Users.Add(user);
        db.Memberships.Add(Membership.Create(user.Id, fixture.OrgA));
        db.GrantExceptions.Add(
            new GrantException
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                UserId = user.Id,
                Domain = "sites",
                Action = "read",
                ScopePath = permitted.GetProperty("path").GetString(),
                Reason = "hierarchy regression",
                GrantedBy = user.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            }
        );
        await db.SaveChangesAsync();
        using var reader = await fixture.LoginAsync(email);
        var tree = await reader.GetFromJsonAsync<JsonElement>("/api/hierarchy");
        var ids = tree.GetProperty("nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("id").GetGuid())
            .ToArray();
        Assert.Contains(root, ids);
        Assert.Contains(permitted.GetProperty("id").GetGuid(), ids);
        Assert.DoesNotContain(hidden.GetProperty("id").GetGuid(), ids);
    }

    [Fact]
    public async Task Session_absolute_age_is_enforced_even_when_the_cookie_is_fresh()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserB);
        await using var db = (IdentityDbContext)
            ApiFixture.CreateCatalogContext(
                LocIntel.Api.ModuleCatalog.AllWithPlatform.Single(m => m.Schema == "identity"),
                fixture.PostgresConnectionString
            );
        var userId = await db
            .Users.Where(u => u.Email == ApiFixture.UserB)
            .Select(u => u.Id)
            .SingleAsync();
        await db
            .Sessions.Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddHours(-13))
            );
        Assert.Equal(
            "guest",
            (await client.GetFromJsonAsync<JsonElement>("/me")).GetProperty("tier").GetString()
        );
    }

    [Fact]
    public async Task Public_responses_cannot_enter_a_shared_cache()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var response = await client.GetAsync("/public/sites");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
    }
}
