using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Platform.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class ContactTierTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Issue_deliver_redeem_yields_contact_principal()
    {
        // Issue: a member of Org A invites a contact
        var member = await fixture.LoginAsync(ApiFixture.UserA);
        var issue = await member.PostAsJsonAsync(
            "/contact-links",
            new { email = "visitor@example.com" }
        );
        issue.EnsureSuccessStatusCode();

        // Deliver: through the outbox -> Wolverine handler -> transport
        var catcher = fixture.Factory.Services.GetRequiredService<LocalMailCatcher>();
        EmailMessage? mail = null;
        for (var i = 0; i < 50 && mail is null; i++) // outbox delivery is async
        {
            await Task.Delay(100);
            mail = catcher.Sent.FirstOrDefault(m => m.To == "visitor@example.com");
        }
        Assert.NotNull(mail);
        var url = System.Text.RegularExpressions.Regex.Match(mail!.TextBody, @"https?://\S+").Value;

        // the link points at the ORG'S public host - the contact's world is
        // the public app
        Assert.StartsWith("http://org-a.localhost:5174/contact/redeem", url);

        // Redeem: fresh browser, no account (the redirect lands on the public
        // app root, which the API test host doesn't serve - the cookie is the
        // point)
        var visitor = fixture.GuestClient();
        await visitor.GetAsync(new Uri(url).PathAndQuery);

        var me = await visitor.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("contact", me.GetProperty("tier").GetString());
        Assert.Equal("visitor@example.com", me.GetProperty("email").GetString());
        Assert.Equal(fixture.OrgA.Value, me.GetProperty("org").GetGuid());
    }

    [Fact]
    public async Task Revoking_a_contact_cuts_off_sessions_and_unexpired_links()
    {
        var member = await fixture.LoginAsync(ApiFixture.UserA);
        (
            await member.PostAsJsonAsync("/contact-links", new { email = "revokee@example.com" })
        ).EnsureSuccessStatusCode();

        var catcher = fixture.Factory.Services.GetRequiredService<LocalMailCatcher>();
        EmailMessage? mail = null;
        for (var i = 0; i < 50 && mail is null; i++)
        {
            await Task.Delay(100);
            mail = catcher.Sent.FirstOrDefault(m => m.To == "revokee@example.com");
        }
        Assert.NotNull(mail);
        var path = new Uri(
            System.Text.RegularExpressions.Regex.Match(mail!.TextBody, @"https?://\S+").Value
        ).PathAndQuery;

        // redeem, prove the identified session works against the public tier
        var visitor = fixture.GuestClient();
        visitor.DefaultRequestHeaders.Add("X-Forwarded-Host", "org-a.locintel.test");
        await visitor.GetAsync(path);
        Assert.Equal(
            "contact",
            (await visitor.GetFromJsonAsync<JsonElement>("/me")).GetProperty("tier").GetString()
        );
        var before = await visitor.GetAsync("/public/sites");
        before.EnsureSuccessStatusCode();

        // the member takes it back
        var contacts = await member.GetFromJsonAsync<JsonElement>("/api/contacts");
        var contactId = contacts
            .EnumerateArray()
            .First(c => c.GetProperty("email").GetString() == "revokee@example.com")
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await member.DeleteAsync($"/api/contacts/{contactId}")).StatusCode
        );

        // the live session's scope collapses to nothing (fail closed - the
        // still-valid cookie now opens no doors) ...
        var after = await visitor.GetFromJsonAsync<JsonElement>("/public/sites");
        Assert.Equal(0, after.GetArrayLength());

        // ... and the unexpired link no longer redeems
        var fresh = fixture.GuestClient();
        var reRedeem = await fresh.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, reRedeem.StatusCode);

        // re-inviting is a deliberate re-grant: same contact, active again
        (
            await member.PostAsJsonAsync("/contact-links", new { email = "revokee@example.com" })
        ).EnsureSuccessStatusCode();
        var relisted = await member.GetFromJsonAsync<JsonElement>("/api/contacts");
        var row = relisted
            .EnumerateArray()
            .First(c => c.GetProperty("email").GetString() == "revokee@example.com");
        Assert.Equal(contactId, row.GetProperty("id").GetGuid());
        Assert.False(row.GetProperty("revoked").GetBoolean());
    }

    [Fact]
    public async Task Contact_custody_needs_roles_manage()
    {
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await viewer.GetAsync("/api/contacts")).StatusCode
        );
    }

    [Fact]
    public async Task Guest_cannot_issue_contact_links()
    {
        var response = await fixture
            .GuestClient()
            .PostAsJsonAsync("/contact-links", new { email = "x@example.com" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var visitor = fixture.GuestClient();
        var response = await visitor.GetAsync("/contact/redeem?token=CfDJ8-tampered");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Guest_receives_session_cookie()
    {
        var response = await fixture.GuestClient().GetAsync("/me");
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("locintel_guest=")
        );
    }
}
