using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace LocIntel.IntegrationTests;

public sealed class TrustedProxyFixture : ApiFixture
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting("Proxy:TrustForwardedHeaders", "true");
        builder.UseSetting("Proxy:KnownProxies:0", "192.0.2.10");
        builder.UseSetting("AllowedHosts", "localhost;*.localhost;*.locintel.test");
    }
}

/// <summary>
/// The gap the final inventory found: behind a TLS-terminating proxy the
/// request arrives as HTTP, and without forwarded-header handling session
/// cookies lose the Secure flag and Request.Scheme-built URLs come out
/// http. With Proxy:TrustForwardedHeaders=true, X-Forwarded-Proto rules.
/// </summary>
public class ForwardedHeadersTests(TrustedProxyFixture fixture) : IClassFixture<TrustedProxyFixture>
{
    [Theory]
    [InlineData("192.0.2.10", "org-a.localhost", "https", "org-a.localhost")]
    [InlineData("192.0.2.20", "org-a.localhost", "http", "localhost")]
    [InlineData("192.0.2.10", "attacker.invalid", "https", "localhost")]
    public async Task Only_known_peers_and_allowed_hosts_can_change_request_authority(
        string peer,
        string forwardedHost,
        string scheme,
        string host
    )
    {
        var context = await fixture.Factory.Server.SendAsync(http =>
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            http.Request.Method = "GET";
            http.Request.Path = "/public/sites";
            http.Request.Scheme = "http";
            http.Request.Host = new Microsoft.AspNetCore.Http.HostString("localhost");
            http.Request.Headers["X-Forwarded-Proto"] = "https";
            http.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        });
        Assert.Equal(scheme, context.Request.Scheme);
        Assert.Equal(host, context.Request.Host.Host);
    }

    [Fact]
    public async Task Cookies_are_secure_when_the_proxy_says_https()
    {
        // any request earns a guest-session cookie; its flags tell the story
        var client = fixture.Factory.CreateDefaultClient();
        var https = new HttpRequestMessage(HttpMethod.Get, "/public/sites");
        https.Headers.Add("X-Forwarded-Proto", "https");
        var overTls = await client.SendAsync(https);
        var secureCookie = overTls
            .Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("locintel_guest"));
        Assert.Contains("secure", secureCookie, StringComparison.OrdinalIgnoreCase);

        var plain = await client.GetAsync("/public/sites");
        var plainCookie = plain
            .Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("locintel_guest"));
        if (plainCookie is not null)
            Assert.DoesNotContain("secure", plainCookie, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Untrusted by default: a spoofed X-Forwarded-Proto changes nothing.</summary>
public class UntrustedProxyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Forwarded_proto_is_ignored_unless_opted_in()
    {
        var client = fixture.Factory.CreateDefaultClient();
        var spoofed = new HttpRequestMessage(HttpMethod.Get, "/public/sites");
        spoofed.Headers.Add("X-Forwarded-Proto", "https");
        var response = await client.SendAsync(spoofed);
        var cookie = response
            .Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("locintel_guest"));
        if (cookie is not null)
            Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
