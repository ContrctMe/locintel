using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LocIntel.IntegrationTests;

public class ConnectionPoolTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task One_connection_budget_is_rejected_before_serving_requests()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:locintel",
                fixture.AppConnectionString + ";Maximum Pool Size=1"
            )
        );
        var error = Assert.Throws<InvalidOperationException>(() => host.Services);
        Assert.Contains("Maximum Pool Size must be at least 2", error.Message);
    }

    [Fact]
    public async Task Concurrent_gated_reads_complete_with_a_small_connection_budget()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:locintel",
                fixture.AppConnectionString + ";Maximum Pool Size=4;Timeout=3"
            )
        );
        using var client = host.CreateDefaultClient(
            new RedirectHandler(),
            new CookieContainerHandler()
        );
        using var login = await client.GetAsync(
            $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserA)}"
        );
        login.EnsureSuccessStatusCode();

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => client.GetAsync("/api/incidents?limit=50", deadline.Token))
        );
        foreach (var response in responses)
        {
            using (response)
                Assert.True(
                    response.IsSuccessStatusCode,
                    await response.Content.ReadAsStringAsync()
                );
        }
    }

    [Fact]
    public async Task Admission_is_bounded_cancellable_and_does_not_block_health()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:locintel",
                fixture.AppConnectionString + ";Maximum Pool Size=4"
            )
        );
        using var client = host.CreateDefaultClient(
            new RedirectHandler(),
            new CookieContainerHandler()
        );
        using var login = await client.GetAsync(
            $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserA)}"
        );
        login.EnsureSuccessStatusCode();
        var limiter = host.Services.GetRequiredService<ConcurrencyLimiter>();
        // Hold real admission permits so queue behavior does not depend on a
        // slow database or sleeps. Requests still cross the production pipeline.
        using var occupied = limiter.AttemptAcquire(2);
        Assert.True(occupied.IsAcquired);
        using var cancelled = new CancellationTokenSource();
        var pending = client.GetAsync("/api/incidents?limit=50", cancelled.Token);
        await ApiFixture.WaitUntilAsync(
            () => Task.FromResult(limiter.GetStatistics()!.CurrentQueuedCount == 1),
            "request queued"
        );
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await ApiFixture.WaitUntilAsync(
            () => Task.FromResult(limiter.GetStatistics()!.CurrentQueuedCount == 0),
            "cancelled request removed"
        );

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var queued = Enumerable
            .Range(0, 8)
            .Select(_ => client.GetAsync("/api/incidents?limit=50", deadline.Token))
            .ToArray();
        await ApiFixture.WaitUntilAsync(
            () => Task.FromResult(limiter.GetStatistics()!.CurrentQueuedCount == 8),
            "admission queue full"
        );
        using var rejected = await client.GetAsync("/api/incidents?limit=50", deadline.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), rejected.Headers.RetryAfter!.Delta);
        foreach (var path in new[] { "/livez", "/healthz" })
        {
            using var health = await client.GetAsync(path, deadline.Token);
            health.EnsureSuccessStatusCode();
        }
        occupied.Dispose();
        foreach (var response in await Task.WhenAll(queued))
        {
            using (response)
                response.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Concurrent_writes_and_reads_keep_transactional_results()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:locintel",
                fixture.AppConnectionString + ";Maximum Pool Size=4;Timeout=3"
            )
        );
        using var client = host.CreateDefaultClient(
            new RedirectHandler(),
            new CookieContainerHandler()
        );
        using var login = await client.GetAsync(
            $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(ApiFixture.UserA)}"
        );
        login.EnsureSuccessStatusCode();
        var root = await ApiFixture.EnsureRootAsync(client);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var work = Enumerable
            .Range(0, 8)
            .Select(i =>
                i % 2 == 0
                    ? client.PostAsJsonAsync(
                        "/api/sites",
                        new
                        {
                            nodeId = root,
                            name = $"Pool site {i}",
                            timeZone = "Etc/UTC",
                        },
                        deadline.Token
                    )
                    : client.GetAsync("/api/incidents?limit=50", deadline.Token)
            );
        foreach (var response in await Task.WhenAll(work))
        {
            using (response)
                response.EnsureSuccessStatusCode();
        }
        var sites = await ApiFixture.GetItemsAsync(client, "/api/sites");
        foreach (var i in new[] { 0, 2, 4, 6 })
            Assert.Contains(
                sites.EnumerateArray(),
                site => site.GetProperty("name").GetString() == $"Pool site {i}"
            );
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData(";Maximum Pool Size=7", 7)]
    [InlineData(";Minimum Pool Size=25", 25)]
    public async Task Runtime_bounds_default_pools_and_preserves_explicit_budgets(
        string suffix,
        int expected
    )
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:locintel", fixture.AppConnectionString + suffix)
        );
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var regions = host.Services.GetRequiredService<IRegionDataSources>();
        Assert.Equal(
            expected,
            new NpgsqlConnectionStringBuilder(
                configuration.GetConnectionString("locintel")
            ).MaxPoolSize
        );
        Assert.Equal(
            expected,
            new NpgsqlConnectionStringBuilder(
                regions.For(RegionId.Default).ConnectionString
            ).MaxPoolSize
        );
    }
}
