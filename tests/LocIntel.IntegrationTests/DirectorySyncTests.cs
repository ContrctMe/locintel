using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Adds the webhook signing secret to the emulator fixture. The emulator
/// cannot emit dsync webhooks, so tests hand-sign WorkOS-format payloads
/// against the adapter's REAL verification path (ADR 41; same approach as
/// the Stripe webhook tests).
/// </summary>
public sealed class DirectorySyncFixture : WorkOSEmulatorFixture
{
    public const string WebhookSecret = "whsec_dsync_test";

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting("Auth:WorkOS:WebhookSecret", WebhookSecret);
    }
}

public class DirectorySyncTests(DirectorySyncFixture fixture) : IClassFixture<DirectorySyncFixture>
{
    /// <summary>The full AuthKit dance, headless (see WorkOSAdapterTests).</summary>
    private async Task<HttpClient> AuthKitLoginAsync(string email)
    {
        var client = fixture.Factory.CreateDefaultClient(new CookieContainerHandler());
        var login = await client.GetAsync($"/auth/login?hint={Uri.EscapeDataString(email)}");
        using var external = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var authorize = await external.GetAsync(login.Headers.Location!);
        var callback = authorize.Headers.Location!;
        var exchanged = await client.GetAsync(callback.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, exchanged.StatusCode); // session issued -> /me
        return client;
    }

    /// <summary>Founder path against the emulator: create, wait for the outbox, switch in.</summary>
    private async Task<(HttpClient Client, Guid OrgId, string ExternalOrgId)> FoundOrgAsync(
        string name,
        string slug
    )
    {
        var client = await AuthKitLoginAsync("alice@acme.test");
        var created = await client.PostAsJsonAsync("/api/orgs", new { name, slug });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("orgId")
            .GetGuid();
        for (var i = 0; i < 100; i++)
        {
            var me = await client.GetFromJsonAsync<JsonElement>("/me");
            if (
                me.GetProperty("organizations")
                    .EnumerateArray()
                    .Any(o => o.GetProperty("id").GetGuid() == orgId)
            )
                break;
            await Task.Delay(100);
        }
        (await client.PostAsJsonAsync("/auth/switch-org", new { orgId })).EnsureSuccessStatusCode();

        // the org's external id lives at the provider; ask the emulator
        using var emulator = new HttpClient();
        emulator.DefaultRequestHeaders.Authorization = new("Bearer", "sk_test_default");
        var orgs = await emulator.GetFromJsonAsync<JsonElement>(
            $"{fixture.EmulatorUrl}/organizations"
        );
        var externalOrgId = orgs.GetProperty("data")
            .EnumerateArray()
            .First(o => o.GetProperty("name").GetString() == name)
            .GetProperty("id")
            .GetString()!;

        // and the OrgDirectory read model learns it via OrganizationUpserted
        for (var i = 0; i < 100; i++)
        {
            var sso = await client.GetFromJsonAsync<JsonElement>("/api/org/sso");
            if (sso.GetProperty("available").GetBoolean())
                break;
            await Task.Delay(100);
        }
        return (client, orgId, externalOrgId);
    }

    private static HttpRequestMessage SignedWebhook(string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(DirectorySyncFixture.WebhookSecret),
                Encoding.UTF8.GetBytes($"{timestamp}.{body}")
            )
        );
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/directory/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("WorkOS-Signature", $"t={timestamp}, v1={signature}");
        return request;
    }

    private static string DsyncUserEvent(
        string eventType,
        string externalOrgId,
        string email,
        string state = "active"
    ) =>
        JsonSerializer.Serialize(
            new
            {
                id = $"event_{Guid.NewGuid():N}",
                @event = eventType,
                data = new
                {
                    id = $"directory_user_{Guid.NewGuid():N}",
                    directory_id = "directory_01TEST",
                    organization_id = externalOrgId,
                    state,
                    emails = new[]
                    {
                        new
                        {
                            primary = true,
                            type = "work",
                            value = email,
                        },
                    },
                    first_name = "Bob",
                    last_name = "Sync",
                    username = email,
                },
                created_at = DateTimeOffset.UtcNow.ToString("O"),
            }
        );

    private async Task<bool> MemberListedAsync(HttpClient admin, string email)
    {
        var members = await ApiFixture.GetItemsAsync(admin, "/api/members");
        return members.EnumerateArray().Any(m => m.GetProperty("email").GetString() == email);
    }

    [Fact]
    public async Task Directory_events_provision_and_deprovision_membership()
    {
        var (admin, _, externalOrgId) = await FoundOrgAsync("Dsync Co", "dsync-co");
        var guest = fixture.GuestClient();

        // joiner: verified upsert -> local user + membership appear
        var created = await guest.SendAsync(
            SignedWebhook(DsyncUserEvent("dsync.user.created", externalOrgId, "bob@dsync.test"))
        );
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var provisioned = false;
        for (var i = 0; i < 200 && !provisioned; i++)
        {
            provisioned = await MemberListedAsync(admin, "bob@dsync.test");
            if (!provisioned)
                await Task.Delay(100);
        }
        Assert.True(provisioned, await fixture.DeadLetterSummary());

        // re-delivery is idempotent
        (
            await guest.SendAsync(
                SignedWebhook(DsyncUserEvent("dsync.user.created", externalOrgId, "bob@dsync.test"))
            )
        ).EnsureSuccessStatusCode();

        // leaver: the IdP's word is final - membership goes
        var deleted = await guest.SendAsync(
            SignedWebhook(DsyncUserEvent("dsync.user.deleted", externalOrgId, "bob@dsync.test"))
        );
        Assert.Equal(HttpStatusCode.Accepted, deleted.StatusCode);
        var gone = false;
        for (var i = 0; i < 200 && !gone; i++)
        {
            gone = !await MemberListedAsync(admin, "bob@dsync.test");
            if (!gone)
                await Task.Delay(100);
        }
        Assert.True(gone, await fixture.DeadLetterSummary());
    }

    [Fact]
    public async Task Unverifiable_or_irrelevant_deliveries_never_reach_a_handler()
    {
        var guest = fixture.GuestClient();

        // wrong signature: 400, no trust in the body
        var forged = new HttpRequestMessage(HttpMethod.Post, "/auth/directory/webhook")
        {
            Content = new StringContent(
                DsyncUserEvent("dsync.user.created", "org_whatever", "evil@dsync.test"),
                Encoding.UTF8,
                "application/json"
            ),
        };
        forged.Headers.Add("WorkOS-Signature", "t=1, v1=deadbeef");
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.SendAsync(forged)).StatusCode);

        // verified but untracked event type: 202 keeps provider retry health green
        var group = await guest.SendAsync(
            SignedWebhook("""{"id":"event_x","event":"dsync.group.created","data":{}}""")
        );
        Assert.Equal(HttpStatusCode.Accepted, group.StatusCode);

        // verified but unknown org: 202, nothing to map to
        var unknown = await guest.SendAsync(
            SignedWebhook(DsyncUserEvent("dsync.user.created", "org_unknown", "x@dsync.test"))
        );
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
    }

    [Fact]
    public async Task Portal_is_entitlement_gated_then_links_to_the_provider()
    {
        var (admin, orgId, _) = await FoundOrgAsync("Portal Co", "portal-co");

        // gate 1: free tier has no sso.enabled -> 402 upsell
        var blocked = await admin.PostAsJsonAsync(
            "/api/org/sso/portal",
            new { intent = "sso", returnPath = "/settings" }
        );
        Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);

        // arrange the entitlement directly (the plan ladder is billing-tested;
        // superuser connection - RLS does not gate the arrange)
        await using (var conn = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO entitlements.org_entitlements (id, org_id, code, value, source, updated_at)
                VALUES ($1, $2, 'sso.enabled', 'true', 'operator', now())
                """,
                conn
            );
            cmd.Parameters.AddWithValue(Guid.CreateVersion7());
            cmd.Parameters.AddWithValue(orgId);
            await cmd.ExecuteNonQueryAsync();
        }

        var sso = await admin.GetFromJsonAsync<JsonElement>("/api/org/sso");
        Assert.True(sso.GetProperty("available").GetBoolean());
        Assert.True(sso.GetProperty("entitled").GetBoolean());

        var linked = await admin.PostAsJsonAsync(
            "/api/org/sso/portal",
            new { intent = "dsync", returnPath = "/settings" }
        );
        Assert.True(linked.IsSuccessStatusCode, await linked.Content.ReadAsStringAsync());
        var url = (await linked.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("url")
            .GetString();
        Assert.StartsWith(fixture.EmulatorUrl, url);
    }
}
