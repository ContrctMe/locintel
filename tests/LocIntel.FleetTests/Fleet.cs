using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.FleetTests;

/// <summary>
/// The stack tools/replica-stack.sh boots: N api and N worker replicas behind
/// one proxy, one Postgres. Black-box - the suite speaks HTTP through the
/// proxy and SQL to the database, and knows nothing else. Every case skips
/// when the stack is not there (LOCINTEL_FLEET_URL unset), so a plain
/// <c>dotnet test</c> over the solution never needs it.
/// </summary>
public sealed class Fleet
{
    public const string Alice = "alice@acme.test";
    public const string Operator = "operator@locintel.local";

    public string? Url { get; } = Environment.GetEnvironmentVariable("LOCINTEL_FLEET_URL");
    public string OwnerConnectionString { get; } =
        Environment.GetEnvironmentVariable("LOCINTEL_FLEET_PG") ?? "";
    public int Replicas { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("LOCINTEL_FLEET_REPLICAS"), out var n)
            ? n
            : 1;
    public int[] WorkerPorts { get; } =
        (Environment.GetEnvironmentVariable("LOCINTEL_FLEET_WORKER_PORTS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();

    public bool Available => Url is { Length: > 0 };

    /// <summary>A signed-in client (the local provider signs in by hint); the proxy spreads its requests.</summary>
    public async Task<HttpClient> LoginAsync(string email)
    {
        var handler = new HttpClientHandler { UseCookies = true, AllowAutoRedirect = true };
        var client = new HttpClient(handler) { BaseAddress = new Uri(Url!) };
        // state changes carry the session cookie, so the CSRF check wants an Origin that matches the host
        client.DefaultRequestHeaders.Add("Origin", Url!.TrimEnd('/'));
        var response = await client.GetAsync(
            $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(email)}"
        );
        response.EnsureSuccessStatusCode();
        return client;
    }

    public static string InstanceOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-LocIntel-Instance", out var values) ? values.First() : "?";

    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        string what,
        TimeSpan? timeout = null
    )
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(500);
        }
        throw new TimeoutException($"timed out waiting for {what}");
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(
            response.IsSuccessStatusCode,
            $"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}"
        );
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<Guid> RootNodeAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/hierarchy")))
            .GetProperty("nodes")
            .EnumerateArray()
            .First(n => n.GetProperty("depth").GetInt32() == 0)
            .GetProperty("id")
            .GetGuid();
}

/// <summary>Runs only against a booted fleet; a plain test run reports the case as skipped.</summary>
public sealed class FleetFactAttribute : FactAttribute
{
    public FleetFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOCINTEL_FLEET_URL")))
            Skip = "needs the replica stack: tools/replica-stack.sh";
    }
}

public static class HttpStatus
{
    public static bool IsRateLimited(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.TooManyRequests;
}
