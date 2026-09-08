using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LocIntel.IntegrationTests;

public class LocalAdmissionTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Overload_rejects_without_queueing_and_recovers_when_work_finishes()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        using var created = await owner.PostAsJsonAsync(
            "/api/api-keys",
            new
            {
                name = "admission-proof",
                roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid(),
            }
        );
        created.EnsureSuccessStatusCode();
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("secret")
            .GetString();
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Traffic:MaxConcurrentRequests", "1");
            builder.UseSetting(
                "ConnectionStrings:locintel",
                fixture.AppConnectionString + ";Maximum Pool Size=1;Timeout=10"
            );
            builder.UseSetting("ConnectionStrings:locintel-messaging", fixture.AppConnectionString);
        });
        using var client = host.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        (await client.GetAsync("/api/sites?limit=1")).EnsureSuccessStatusCode();
        var regions = host.Services.GetRequiredService<IRegionDataSources>();
        await using var occupied = await regions.For(RegionId.Default).OpenConnectionAsync();
        var pending = client.GetAsync("/api/sites?limit=1");
        var limiter = host
            .Services.GetRequiredService<IOptions<RateLimiterOptions>>()
            .Value.GlobalLimiter!;
        await ApiFixture.WaitUntilAsync(
            () =>
                Task.FromResult(
                    limiter.GetStatistics(new DefaultHttpContext())?.CurrentAvailablePermits == 0
                ),
            "the request to hold the process admission permit"
        );
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var rejected = await client.GetAsync("/api/sites?limit=1", deadline.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), rejected.Headers.RetryAfter?.Delta);
        (await client.GetAsync("/livez", deadline.Token)).EnsureSuccessStatusCode();
        await occupied.CloseAsync();
        using var completed = await pending;
        completed.EnsureSuccessStatusCode();
        (await client.GetAsync("/api/sites?limit=1")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Request_rates_are_absent_from_billing_and_cannot_be_changed_as_an_entitlement()
    {
        Assert.False(
            LocIntel.Platform.Entitlements.EntitlementCatalog.Definitions.ContainsKey(
                "api.requests_per_minute"
            )
        );
        using var op = await fixture.OperatorClient();
        using var response = await op.PutAsJsonAsync(
            $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/api.requests_per_minute",
            new { value = "1" }
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
