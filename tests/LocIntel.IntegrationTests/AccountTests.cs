using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Platform.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Account self-service: the user acting on themselves. Sessions are the new
/// enforcement piece - the cookie is self-contained, so the server-side
/// record is what revocation actually revokes.
/// </summary>
public class AccountTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Profile_rename_shows_up_in_me_immediately()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserBoth);
        var renamed = await client.PutAsJsonAsync("/auth/profile", new { name = "Renamed Person" });
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        // the cookie was re-issued: the very next read carries the new name
        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("Renamed Person", me.GetProperty("name").GetString());

        var tooLong = await client.PutAsJsonAsync(
            "/auth/profile",
            new { name = new string('x', 201) }
        );
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Password_reset_delivers_the_provider_minted_link()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var response = await client.PostAsync("/auth/password-reset", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var catcher = fixture.Factory.Services.GetRequiredService<LocalMailCatcher>();
        var mail = catcher.Sent.FirstOrDefault(m =>
            m.To == ApiFixture.UserA && m.Subject == "Reset your password"
        );
        Assert.NotNull(mail);
        Assert.Contains("reset", mail.TextBody);
    }

    [Fact]
    public async Task Revoking_a_session_kills_that_cookie_and_only_that_cookie()
    {
        // the same human, two browsers
        var laptop = await fixture.LoginAsync(ApiFixture.ViewerA);
        var phone = await fixture.LoginAsync(ApiFixture.ViewerA);

        var sessions = await laptop.GetFromJsonAsync<JsonElement>("/auth/sessions");
        Assert.True(sessions.GetArrayLength() >= 2);
        var current = sessions
            .EnumerateArray()
            .First(s => s.GetProperty("current").GetBoolean())
            .GetProperty("id")
            .GetGuid();
        var other = sessions
            .EnumerateArray()
            .First(s => !s.GetProperty("current").GetBoolean())
            .GetProperty("id")
            .GetGuid();

        var revoked = await laptop.DeleteAsync($"/auth/sessions/{other}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        // the phone's cookie is now a signed-out guest; the laptop still works
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await phone.PostAsync("/auth/password-reset", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await laptop.PostAsync("/auth/password-reset", null)).StatusCode
        );

        // revoking someone ELSE's session is a 404, never a confirmation
        var stranger = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.DeleteAsync($"/auth/sessions/{current}")).StatusCode
        );
    }

    [Fact]
    public async Task Revoke_others_keeps_only_the_current_session()
    {
        var keeper = await fixture.LoginAsync(ApiFixture.UserBoth);
        var doomed1 = await fixture.LoginAsync(ApiFixture.UserBoth);
        var doomed2 = await fixture.LoginAsync(ApiFixture.UserBoth);

        var result = await keeper.PostAsync("/auth/sessions/revoke-others", null);
        result.EnsureSuccessStatusCode();

        var remaining = await keeper.GetFromJsonAsync<JsonElement>("/auth/sessions");
        Assert.Equal(1, remaining.GetArrayLength());
        Assert.True(remaining[0].GetProperty("current").GetBoolean());
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await doomed1.GetAsync("/auth/sessions")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await doomed2.GetAsync("/auth/sessions")).StatusCode
        );
    }

    [Fact]
    public async Task Account_deletion_is_blocked_for_a_last_manager_and_total_otherwise()
    {
        // a founder who alone manages an org: deletion must refuse, naming it
        await fixture.CreateUserOnly("solo-founder@locintel.local");
        var lastManager = await fixture.LoginAsync("solo-founder@locintel.local");
        var created = await lastManager.PostAsJsonAsync(
            "/api/orgs",
            new { name = "Solo Co", slug = "solo-co" }
        );
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        // founder membership + Owner arrive via the outbox
        await ApiFixture.WaitForMembershipAsync(lastManager);
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("orgId")
            .GetGuid();
        (
            await lastManager.PostAsJsonAsync("/auth/switch-org", new { orgId })
        ).EnsureSuccessStatusCode();

        var blocked = await lastManager.DeleteAsync("/auth/account");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("last_manager", body.GetProperty("code").GetString());
        Assert.Contains(
            "Solo Co",
            body.GetProperty("organizations").EnumerateArray().Select(o => o.GetString())
        );

        // a member who manages nothing alone deletes cleanly
        await fixture.CreateUserOnly("ephemeral@locintel.local");
        var member = await fixture.LoginAsync("ephemeral@locintel.local");
        var deleted = await member.DeleteAsync("/auth/account");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // cookie dead, sessions dead, record gone
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await member.GetAsync("/auth/sessions")).StatusCode
        );
        var me = await member.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("guest", me.GetProperty("tier").GetString());
    }
}
