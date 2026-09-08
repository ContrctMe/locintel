using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Api;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class GatewayIdentityTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string IdentityKey = "gateway-identity-test-secret-at-least-32-bytes";

    [Fact]
    public async Task Local_provider_organization_ids_do_not_collide_across_hosts()
    {
        var ids = new HashSet<Guid>();
        foreach (var index in new[] { 1, 2 })
        {
            await using var host = IdentityHost();
            using var client = host.CreateDefaultClient(
                new Microsoft.AspNetCore.Mvc.Testing.Handlers.RedirectHandler(),
                new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler()
            );
            (
                await client.GetAsync(
                    $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserA)}"
                )
            ).EnsureSuccessStatusCode();
            using var created = await client.PostAsJsonAsync(
                "/api/orgs",
                new
                {
                    name = "Same display name on separate replicas",
                    slug = $"gateway-host-{index}-{Guid.NewGuid():N}",
                }
            );
            created.EnsureSuccessStatusCode();
            ids.Add(
                (await created.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("orgId")
                    .GetGuid()
            );
        }
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task Real_sessions_follow_org_switches_and_ignore_client_partition_headers()
    {
        await using var host = IdentityHost();
        using var client = host.CreateDefaultClient(
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.RedirectHandler(),
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler()
        );
        (
            await client.GetAsync(
                $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserBoth)}"
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(Org(fixture.OrgA), await Partition(client));
        (
            await client.PostAsJsonAsync("/auth/switch-org", new { orgId = fixture.OrgB.Value })
        ).EnsureSuccessStatusCode();
        Assert.Equal(Org(fixture.OrgB), await Partition(client));
        client.DefaultRequestHeaders.Add(GatewayIdentityCache.PartitionHeader, Org(fixture.OrgA));
        Assert.Equal(Org(fixture.OrgB), await Partition(client));
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            "locintel_invalid-but-cookie-takes-precedence"
        );
        Assert.Equal(Org(fixture.OrgB), await Partition(client));
        Assert.Equal(
            fixture.OrgB.Value,
            (await client.GetFromJsonAsync<JsonElement>("/me")).GetProperty("activeOrg").GetGuid()
        );
    }

    [Fact]
    public async Task Contact_and_impersonation_sessions_use_the_active_tenant()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        const string email = "gateway-contact@example.test";
        (await owner.PostAsJsonAsync("/contact-links", new { email })).EnsureSuccessStatusCode();
        var catcher =
            fixture.Factory.Services.GetRequiredService<LocIntel.Platform.Notifications.LocalMailCatcher>();
        var mail = await ApiFixture.WaitForAsync(
            () => Task.FromResult(catcher.Sent.FirstOrDefault(m => m.To == email)),
            "the gateway proof contact link"
        );
        var url = System.Text.RegularExpressions.Regex.Match(mail.TextBody, @"https?://\S+").Value;
        await using var host = IdentityHost();
        using var contact = host.CreateDefaultClient(
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.RedirectHandler(),
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler()
        );
        await contact.GetAsync(new Uri(url).PathAndQuery);
        Assert.Equal(
            "contact",
            (await contact.GetFromJsonAsync<JsonElement>("/me")).GetProperty("tier").GetString()
        );
        Assert.Equal(Org(fixture.OrgA), await Partition(contact));
        using var op = host.CreateDefaultClient(
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.RedirectHandler(),
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler()
        );
        (
            await op.GetAsync(
                $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.Operator)}"
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(Org(fixture.PlatformOrg), await Partition(op));
        (
            await op.PostAsync($"/api/operator/orgs/{fixture.OrgB.Value}/impersonate", null)
        ).EnsureSuccessStatusCode();
        Assert.Equal(Org(fixture.OrgB), await Partition(op));
        (await op.PostAsync("/auth/impersonation/stop", null)).EnsureSuccessStatusCode();
        Assert.Equal(Org(fixture.PlatformOrg), await Partition(op));
    }

    [Fact]
    public async Task Gateway_required_rejects_direct_business_requests_but_keeps_probes_available()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Gateway:IdentityKey", IdentityKey);
            builder.UseSetting("Gateway:Required", "true");
        });
        using var client = host.CreateDefaultClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/livez")).StatusCode);
        client.DefaultRequestHeaders.Add(GatewayIdentityCache.KeyHeader, IdentityKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task Different_keys_share_tenant_partition_and_cached_identity_never_grants_access()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var first = await CreateKey(owner);
        var second = await CreateKey(owner);
        await using var host = IdentityHost();
        using var service = host.CreateDefaultClient();
        service.DefaultRequestHeaders.Authorization = new("Bearer", first.secret);
        Assert.Equal(Org(fixture.OrgA), await Partition(service));
        service.DefaultRequestHeaders.Authorization = new("Bearer", second.secret);
        Assert.Equal(Org(fixture.OrgA), await Partition(service));

        (await owner.DeleteAsync($"/api/api-keys/{second.id}")).EnsureSuccessStatusCode();
        // A stale fairness classification is allowed; it is never an auth ticket.
        Assert.Equal(Org(fixture.OrgA), await Partition(service));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await service.GetAsync("/api/sites?limit=1")).StatusCode
        );
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                using var request = IdentityRequest();
                using var response = await service.SendAsync(request);
                return response.StatusCode == HttpStatusCode.Unauthorized;
            },
            "the revoked key's fairness classification to expire"
        );
    }

    [Fact]
    public async Task Warm_identity_lookup_does_not_need_a_database_connection()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var key = await CreateKey(owner);
        await using var host = IdentityHost(singleConnection: true);
        using var service = host.CreateDefaultClient();
        service.DefaultRequestHeaders.Authorization = new("Bearer", key.secret);
        Assert.Equal(Org(fixture.OrgA), await Partition(service));
        var sources = host.Services.GetRequiredService<IRegionDataSources>();
        await using var held = await sources.For(RegionId.Default).OpenConnectionAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var request = IdentityRequest();
        using var response = await service.SendAsync(request, deadline.Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            Org(fixture.OrgA),
            response.Headers.GetValues(GatewayIdentityCache.PartitionHeader).Single()
        );
    }

    [Fact]
    public async Task Private_lookup_requires_gateway_secret_and_guests_cannot_choose_a_partition()
    {
        await using var host = IdentityHost();
        using var client = host.CreateDefaultClient();
        using var unauthenticated = await client.GetAsync(GatewayIdentityCache.Path);
        Assert.Equal(HttpStatusCode.NotFound, unauthenticated.StatusCode);
        client.DefaultRequestHeaders.Add(GatewayIdentityCache.PartitionHeader, Org(fixture.OrgA));
        client.DefaultRequestHeaders.Add("Cookie", "locintel_guest=chosen-by-attacker");
        Assert.Equal("ip:192.0.2.1", await Partition(client));
        using var request = IdentityRequest();
        request.Headers.Remove(GatewayIdentityCache.ClientIpHeader);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "locintel_not-a-real-key");
        using var invalid = IdentityRequest();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(invalid)).StatusCode);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> IdentityHost(
        bool singleConnection = false
    ) =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Gateway:IdentityKey", IdentityKey);
            if (singleConnection)
            {
                builder.UseSetting(
                    "ConnectionStrings:locintel",
                    fixture.AppConnectionString + ";Maximum Pool Size=1;Timeout=3"
                );
                builder.UseSetting(
                    "ConnectionStrings:locintel-messaging",
                    fixture.AppConnectionString
                );
            }
        });

    private static string Org(OrgId org) => $"org:{org.Value:D}";

    private static HttpRequestMessage IdentityRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, GatewayIdentityCache.Path);
        request.Headers.Add(GatewayIdentityCache.KeyHeader, IdentityKey);
        request.Headers.Add(GatewayIdentityCache.ClientIpHeader, "192.0.2.1");
        return request;
    }

    private static async Task<string> Partition(HttpClient client)
    {
        using var request = IdentityRequest();
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues(GatewayIdentityCache.PartitionHeader).Single();
    }

    private static async Task<(Guid id, string secret)> CreateKey(HttpClient owner)
    {
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        using var response = await owner.PostAsJsonAsync(
            "/api/api-keys",
            new
            {
                name = $"gateway-proof-{Guid.NewGuid():N}",
                roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid(),
            }
        );
        response.EnsureSuccessStatusCode();
        var key = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (key.GetProperty("id").GetGuid(), key.GetProperty("secret").GetString()!);
    }
}
