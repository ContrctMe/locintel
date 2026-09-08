using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class HostLifetimeTests
{
    [Fact]
    public async Task Disposed_hosts_are_collectible()
    {
        var hosts = new List<WeakReference>();
        for (var i = 0; i < 3; i++)
            hosts.AddRange(await StartAndDisposeHost());

        // Allow shutdown continuations to unwind before checking ownership.
        await ApiFixture.WaitUntilAsync(
            () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                return Task.FromResult(hosts.All(host => !host.IsAlive));
            },
            "disposed hosts and limiter timers to become collectible",
            TimeSpan.FromSeconds(1),
            diagnostics: () =>
                Task.FromResult(
                    string.Join(
                        ", ",
                        hosts
                            .Select((host, index) => (host, index))
                            .Where(item => item.host.IsAlive)
                            .Select(item =>
                                $"host {item.index / 4}, retained {new[] { "provider", "chain", "org limiter", "principal limiter" }[item.index % 4]}"
                            )
                    )
                )
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> StartAndDisposeHost()
    {
        var fixture = new ApiFixture();
        try
        {
            await fixture.InitializeAsync();
            using var client = await fixture.LoginAsync(ApiFixture.UserA);
            using var response = await client.GetAsync("/api/roles");
            response.EnsureSuccessStatusCode();
            // A disposed chain doesn't own its children. Check their collection
            // independently: a leaked timer can survive even without a host root.
            return
            [
                new(fixture.Factory.Services),
                new(
                    fixture.Factory.Services.GetRequiredService<
                        PartitionedRateLimiter<HttpContext>
                    >()
                ),
                new(
                    fixture.Factory.Services.GetRequiredKeyedService<
                        PartitionedRateLimiter<HttpContext>
                    >("org")
                ),
                new(
                    fixture.Factory.Services.GetRequiredKeyedService<
                        PartitionedRateLimiter<HttpContext>
                    >("principal")
                ),
            ];
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
