using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The HTTP hardening floor (operability item 8): every response carries the
/// security headers and a trace identifier. Admission behavior is covered by
/// LocalAdmissionTests and the real gateway suite.
/// </summary>
public class HttpHardeningTests(ApiFixture fixture) : IClassFixture<ApiFixture>
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
