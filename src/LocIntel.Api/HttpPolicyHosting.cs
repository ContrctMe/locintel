using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using LocIntel.Modules.Identity.Auth;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LocIntel.Api;

internal static class HttpPolicyHosting
{
    public static void AddRequestPolicies(this WebApplicationBuilder builder)
    {
        var pool = new NpgsqlConnectionStringBuilder(
            builder.Configuration.GetConnectionString("locintel")
        );
        if (pool.MaxPoolSize < 2)
            throw new InvalidOperationException(
                "Maximum Pool Size must be at least 2: transactional requests also query authorization."
            );
        // An eager transaction holds one connection while gates query another
        // context. Admit work BEFORE any connection opens, leaving half the
        // pool available for those queries and background work.
        builder.Services.AddSingleton<ConcurrencyLimiter>(_ =>
            new(
                new ConcurrencyLimiterOptions
                {
                    PermitLimit = pool.MaxPoolSize / 2,
                    QueueLimit = pool.MaxPoolSize * 2,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }
            )
        );
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
                context
                    .HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware")
                    .LogDebug(
                        new EventId(1, "RequestRejectedLimitsExceeded"),
                        "Rate limits exceeded, rejecting this request."
                    );
                var seconds = context.Lease.TryGetMetadata(
                    System.Threading.RateLimiting.MetadataName.RetryAfter,
                    out var retryAfter
                )
                    ? Math.Max(1, (int)retryAfter.TotalSeconds)
                    : 60;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
                return ValueTask.CompletedTask;
            };
        });
        // A chained limiter does NOT dispose its children. DI owns all three.
        builder.Services.AddKeyedSingleton<PartitionedRateLimiter<HttpContext>>(
            "org",
            (_, _) =>
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
                })
        );
        builder.Services.AddKeyedSingleton<PartitionedRateLimiter<HttpContext>>(
            "principal",
            (_, _) =>
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
        builder.Services.AddSingleton<PartitionedRateLimiter<HttpContext>>(sp =>
            PartitionedRateLimiter.CreateChained(
                sp.GetRequiredKeyedService<PartitionedRateLimiter<HttpContext>>("org"),
                sp.GetRequiredKeyedService<PartitionedRateLimiter<HttpContext>>("principal")
            )
        );
        builder
            .Services.AddOptions<RateLimiterOptions>()
            .Configure<PartitionedRateLimiter<HttpContext>>(
                (options, global) => options.GlobalLimiter = global
            );
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
                var admission = app.Services.GetRequiredService<ConcurrencyLimiter>();
                api.Use(
                    async (http, next) =>
                    {
                        using var lease = await admission.AcquireAsync(1, http.RequestAborted);
                        if (!lease.IsAcquired)
                        {
                            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                            http.Response.Headers.RetryAfter = "1";
                            return;
                        }
                        await next(http);
                    }
                );
                api.UseMiddleware<PublicCacheMiddleware>();
                api.UseAuthentication();
                api.UseMiddleware<SessionValidationMiddleware>();
                api.UseMiddleware<ApiKeyAuthenticationMiddleware>();
                api.UseMiddleware<CsrfOriginMiddleware>();
                api.UseMiddleware<GuestSessionMiddleware>();
                api.UseMiddleware<GuestOrgMiddleware>();
                api.UseMiddleware<SessionContextMiddleware>();
                // Global quotas only: the framework middleware also creates an
                // unused endpoint limiter that never gets disposed (aspnetcore
                // #66434). Keep the native limiter, but let DI own its lifetime.
                // Preserve the framework's global-policy instruments. There are
                // no endpoint policies or queues in this application.
                var limiter = app.Services.GetRequiredService<
                    PartitionedRateLimiter<HttpContext>
                >();
                var meter = app
                    .Services.GetRequiredService<IMeterFactory>()
                    .Create("Microsoft.AspNetCore.RateLimiting");
                var active = meter.CreateUpDownCounter<long>(
                    "aspnetcore.rate_limiting.active_request_leases",
                    "{request}"
                );
                var duration = meter.CreateHistogram<double>(
                    "aspnetcore.rate_limiting.request_lease.duration",
                    "s"
                );
                var requests = meter.CreateCounter<long>(
                    "aspnetcore.rate_limiting.requests",
                    "{request}"
                );
                meter.CreateUpDownCounter<long>(
                    "aspnetcore.rate_limiting.queued_requests",
                    "{request}"
                );
                meter.CreateHistogram<double>(
                    "aspnetcore.rate_limiting.request.time_in_queue",
                    "s"
                );
                api.Use(
                    async (http, next) =>
                    {
                        // Every configured partition has QueueLimit = 0.
                        using var lease = limiter.AttemptAcquire(http);
                        requests.Add(
                            1,
                            new KeyValuePair<string, object?>(
                                "aspnetcore.rate_limiting.result",
                                lease.IsAcquired ? "acquired" : "global_limiter"
                            )
                        );
                        if (lease.IsAcquired)
                        {
                            var countActive = active.Enabled;
                            var start = Stopwatch.GetTimestamp();
                            if (countActive)
                                active.Add(1);
                            try
                            {
                                await next(http);
                            }
                            finally
                            {
                                if (countActive)
                                    active.Add(-1);
                                duration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds);
                            }
                        }
                        else
                        {
                            var options = http
                                .RequestServices.GetRequiredService<IOptions<RateLimiterOptions>>()
                                .Value;
                            http.Response.StatusCode = options.RejectionStatusCode;
                            await options.OnRejected!(
                                new OnRejectedContext { HttpContext = http, Lease = lease },
                                http.RequestAborted
                            );
                        }
                    }
                );
                api.UseMiddleware<SuspensionMiddleware>();
                api.UseMiddleware<IdempotencyMiddleware>();
                api.UseMiddleware<AccessLogMiddleware>();
            }
        );
    }
}
