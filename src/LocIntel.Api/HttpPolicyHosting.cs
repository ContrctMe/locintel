using System.Threading.RateLimiting;
using LocIntel.Modules.Identity.Auth;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.HttpOverrides;

namespace LocIntel.Api;

internal static class HttpPolicyHosting
{
    public static void AddRequestPolicies(this WebApplicationBuilder builder)
    {
        // Rate limiting (ADR 30): partitioned by principal tier. Guests limit on
        // their session cookie (fallback: IP), users on user id. The per-org quota
        // reading metered entitlements attaches in step 4.
        var guestLimit = builder.Configuration.GetValue("RateLimits:GuestPerMinute", 60);
        var userLimit = builder.Configuration.GetValue("RateLimits:UserPerMinute", 300);
        builder.Services.AddSingleton<OrgRateLimitCache>();
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // consumers deserve to know when to come back: fixed one-minute windows,
            // so the limiter's own retry hint (when present) or the window size
            limiter.OnRejected = (context, _) =>
            {
                var seconds = context.Lease.TryGetMetadata(
                    System.Threading.RateLimiting.MetadataName.RetryAfter,
                    out var retryAfter
                )
                    ? Math.Max(1, (int)retryAfter.TotalSeconds)
                    : 60;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
                return ValueTask.CompletedTask;
            };
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                // ADR 30: org-level quota from the metered entitlement, over the per-principal limiter
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    // ONE resolver for "who is this request": the same Principal the
                    // endpoints see. This lambda used to re-parse claims and Items
                    // itself, and the two readings drifted - API keys fell into the
                    // per-IP guest bucket and skipped the org quota entirely.
                    var principal = http
                        .RequestServices.GetRequiredService<IPrincipalAccessor>()
                        .Current;
                    OrgId? org = principal switch
                    {
                        Principal.User { ActiveOrg: { } active } => active,
                        Principal.Service service => service.Org,
                        Principal.Contact contact => contact.Org,
                        _ => null,
                    };
                    if (org is { } quotaOrg)
                    {
                        var orgGuid = quotaOrg.Value;
                        var orgLimit = http
                            .RequestServices.GetRequiredService<OrgRateLimitCache>()
                            .LimitFor(quotaOrg);
                        // the limit is part of the KEY: partition limiters are
                        // created once and cached, so a quota change must roll to a
                        // fresh partition or a hot org keeps its old limit forever
                        // (found by the load baseline)
                        return RateLimitPartition.GetFixedWindowLimiter(
                            $"org:{orgGuid}:{orgLimit}",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = orgLimit,
                                Window = TimeSpan.FromMinutes(1),
                                QueueLimit = 0,
                            }
                        );
                    }
                    return RateLimitPartition.GetNoLimiter("org:none");
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    var (key, permits) = http
                        .RequestServices.GetRequiredService<IPrincipalAccessor>()
                        .Current switch
                    {
                        // an API key is a first-class principal (ADR 40): its own
                        // bucket at the USER limit, never the per-IP guest bucket
                        Principal.Service service => ($"key:{service.KeyId}", userLimit),
                        Principal.User user => ($"user:{user.UserId}", userLimit),
                        _ => http.Request.Cookies.TryGetValue(
                            GuestSessionMiddleware.CookieName,
                            out var guest
                        )
                            ? ($"guest:{guest}", guestLimit)
                            : ($"ip:{http.Connection.RemoteIpAddress}", guestLimit),
                    };
                    return RateLimitPartition.GetFixedWindowLimiter(
                        key,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = permits,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                    );
                })
            );
        });
    }

    public static void UseRequestPolicies(this WebApplication app)
    {
        // Behind the documented TLS-terminating proxy the request arrives as
        // HTTP: without this, cookies lose the Secure flag and every URL built
        // from Request.Scheme/Host (billing returns, SSO portal returns) comes
        // out http://. Opt-in because trusting these headers from an UNKNOWN
        // peer lets clients spoof scheme/host/ip - only enable it when the
        // immediate proxy strips inbound X-Forwarded-* (reverse proxies do).
        if (app.Configuration.GetValue("Proxy:TrustForwardedHeaders", false))
        {
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor
                    | ForwardedHeaders.XForwardedProto
                    | ForwardedHeaders.XForwardedHost,
            };
            forwarded.KnownIPNetworks.Clear(); // trust the immediate peer: the proxy
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }
        app.UseMiddleware<UnhandledErrorMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseWhen(
            context => context.Request.Path != "/livez" && context.Request.Path != "/healthz",
            api =>
            {
                api.UseMiddleware<PublicCacheMiddleware>();
                api.UseAuthentication();
                api.UseMiddleware<SessionValidationMiddleware>();
                api.UseMiddleware<ApiKeyAuthenticationMiddleware>();
                api.UseMiddleware<CsrfOriginMiddleware>();
                api.UseMiddleware<GuestSessionMiddleware>();
                api.UseMiddleware<GuestOrgMiddleware>();
                api.UseMiddleware<SessionContextMiddleware>();
                api.UseRateLimiter();
                api.UseMiddleware<SuspensionMiddleware>();
                api.UseMiddleware<IdempotencyMiddleware>();
                api.UseMiddleware<AccessLogMiddleware>();
            }
        );
    }
}
