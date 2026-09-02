using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;

namespace LocIntel.IntegrationTests;

public sealed class TinyRateLimitFixture : ApiFixture
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting("RateLimits:GuestPerMinute", "3");
    }
}

/// <summary>
/// The HTTP hardening floor (operability item 8): every response carries the
/// security headers, and a 429 tells the consumer when to come back.
/// </summary>
public class HttpHardeningTests(TinyRateLimitFixture fixture) : IClassFixture<TinyRateLimitFixture>
{
    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        var response = await fixture.GuestClient().GetAsync("/healthz");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal(
            "strict-origin-when-cross-origin",
            response.Headers.GetValues("Referrer-Policy").Single()
        );
        Assert.Contains(
            "default-src 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single()
        );
    }

    [Fact]
    public async Task Exhausted_limit_answers_429_with_retry_after()
    {
        var guest = fixture.GuestClient();
        HttpResponseMessage? limited = null;
        for (var i = 0; i < 10 && limited is null; i++)
        {
            var response = await guest.GetAsync("/api/sites");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                limited = response;
        }
        Assert.NotNull(limited);
        Assert.NotNull(limited!.Headers.RetryAfter);
        Assert.InRange(limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
    }

    [Fact]
    public async Task Every_response_carries_a_trace_id_and_500s_quote_it()
    {
        var guest = fixture.GuestClient();
        var ok = await guest.GetAsync("/healthz");
        Assert.True(ok.Headers.Contains("X-Trace-Id"));
        // "what version are you running?" is answerable (hole 4)
        var health = await ok.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(health.GetProperty("version").GetString()));

        // the deliberate dev failure: the body's traceId matches the header,
        // so a ticket quoting it joins straight to the exported trace
        var boom = await guest.GetAsync("/dev/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, boom.StatusCode);
        var body = await boom.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var quoted = body.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(quoted));
        Assert.Equal(quoted, boom.Headers.GetValues("X-Trace-Id").Single());
    }
}
