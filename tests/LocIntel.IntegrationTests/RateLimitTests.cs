using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The two limiter defects the load baseline found, pinned. Own fixture on
/// purpose: limiter buckets are shared per-process, so these tests must not
/// share a tiny-limit fixture with anything else.
/// </summary>
public class RateLimitTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Api_keys_get_their_own_bucket_not_the_guest_ip_bucket()
    {
        // service principals carried no claims and no guest cookie, so they
        // throttled as anonymous IPs (guest default: 60/min). 70 straight
        // successes proves the key rides its own USER-limit bucket.
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        var roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await owner.PostAsJsonAsync("/api/api-keys", new { name = "limits", roleId });
        created.EnsureSuccessStatusCode();
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("secret")
            .GetString()!;

        var service = fixture.Factory.CreateDefaultClient();
        service.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        for (var i = 0; i < 70; i++)
            (await service.GetAsync("/api/sites?limit=1")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Org_quota_entitlement_actually_reaches_the_limiter()
    {
        // the cache refresh scope carried no tenant, RLS hid the org row,
        // and every org ran at the catalog default forever - AND the
        // partition limiter baked in whatever limit it was born with. A
        // 3/min quota must eventually 429 a burst.
        var op = await fixture.OperatorClient();
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{fixture.OrgB.Value}/entitlements/api.requests_per_minute",
                new { value = "3" }
            )
        ).EnsureSuccessStatusCode();

        var member = await fixture.LoginAsync(ApiFixture.UserB);
        var limited = false;
        for (var i = 0; i < 100 && !limited; i++)
        {
            for (var burst = 0; burst < 5 && !limited; burst++)
                limited =
                    (await member.GetAsync("/api/sites?limit=1")).StatusCode
                    == HttpStatusCode.TooManyRequests;
            if (!limited)
                await Task.Delay(200);
        }
        Assert.True(limited, "the 3/min org quota never reached the limiter");
    }

    [Fact]
    public async Task A_contact_session_is_quota_limited_through_the_same_resolver_as_endpoints()
    {
        // Candidate 2 (architecture review): the limiter used to re-parse
        // claims itself, so "which org does this request belong to" had two
        // readings that could drift. It now keys off the same Principal the
        // endpoints see - proven with the one tier that carries NO user id:
        // a contact must still land in its org's quota bucket.
        //
        // A PRIVATE org: quota buckets are per-org windows shared across the
        // fixture, and bursting Org B here starved its neighbour's setup.
        await fixture.CreateUserOnly("quota-founder@locintel.local");
        var founder = await fixture.LoginAsync("quota-founder@locintel.local");
        var created = await founder.PostAsJsonAsync(
            "/api/orgs",
            new { name = "Quota Co", slug = "quota-co" }
        );
        created.EnsureSuccessStatusCode();
        var orgId = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("orgId")
            .GetGuid();
        await ApiFixture.WaitForMembershipAsync(founder);
        (
            await founder.PostAsJsonAsync("/auth/switch-org", new { orgId })
        ).EnsureSuccessStatusCode();

        var op = await fixture.OperatorClient();
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{orgId}/entitlements/api.requests_per_minute",
                new { value = "3" }
            )
        ).EnsureSuccessStatusCode();

        (
            await founder.PostAsJsonAsync(
                "/contact-links",
                new { email = "quota-contact@example.com" }
            )
        ).EnsureSuccessStatusCode();
        var catcher =
            fixture.Factory.Services.GetRequiredService<LocIntel.Platform.Notifications.LocalMailCatcher>();
        var mail = await ApiFixture.WaitForAsync(
            () =>
                Task.FromResult(
                    catcher.Sent.FirstOrDefault(m => m.To == "quota-contact@example.com")
                ),
            "the contact link to be delivered"
        );
        var url = System.Text.RegularExpressions.Regex.Match(mail.TextBody, @"https?://\S+").Value;
        var contact = fixture.GuestClient();
        await contact.GetAsync(new Uri(url).PathAndQuery);
        Assert.Equal(
            "contact",
            (await contact.GetFromJsonAsync<System.Text.Json.JsonElement>("/me"))
                .GetProperty("tier")
                .GetString()
        );

        var limited = false;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                for (var burst = 0; burst < 5 && !limited; burst++)
                    limited =
                        (await contact.GetAsync("/me")).StatusCode
                        == HttpStatusCode.TooManyRequests;
                return limited;
            },
            "the contact to hit its org's 3/min quota"
        );
    }
}
