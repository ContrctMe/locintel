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

        // Use the shared bounded wait for shutdown continuations; collection is
        // an ownership assertion, not a one-second latency guarantee under CI load.
        await ApiFixture.WaitUntilAsync(
            () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                return Task.FromResult(hosts.All(host => !host.IsAlive));
            },
            "disposed hosts and limiter timers to become collectible",
            diagnostics: () =>
                Task.FromResult(
                    string.Join(
                        ", ",
                        hosts
                            .Select((host, index) => (host, index))
                            .Where(item => item.host.IsAlive)
                            .Select(item =>
                                $"host {item.index / 2}, retained {new[] { "provider", "process limiter" }[item.index % 2]}"
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
            // Check the process limiter independently from the disposed host.
            return
            [
                new(fixture.Factory.Services),
                new(
                    fixture.Factory.Services.GetRequiredService<
                        PartitionedRateLimiter<HttpContext>
                    >()
                ),
            ];
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
