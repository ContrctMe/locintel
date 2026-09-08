using System.Net;
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
        var trustForwarded = builder.Configuration.GetValue("Proxy:TrustForwardedHeaders", false);
        var allowedHosts = (builder.Configuration["AllowedHosts"] ?? "").Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (
            (
                !ProviderOptionsValidation.IsDevelopmentOrTesting(builder.Environment)
                || trustForwarded
            ) && (allowedHosts.Length == 0 || allowedHosts.Contains("*"))
        )
            throw new InvalidOperationException(
                "AllowedHosts must list the application hosts; '*' is not allowed outside Development/Testing or when trusting a proxy."
            );
        if (trustForwarded)
        {
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor
                    | ForwardedHeaders.XForwardedProto
                    | ForwardedHeaders.XForwardedHost,
                ForwardLimit = 1,
            };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            foreach (
                var value in builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>()
                    ?? []
            )
            {
                if (
                    !IPAddress.TryParse(value, out var address)
                    || address.Equals(IPAddress.Any)
                    || address.Equals(IPAddress.IPv6Any)
                )
                    throw new InvalidOperationException(
                        "Proxy:KnownProxies must contain explicit IP addresses."
                    );
                forwarded.KnownProxies.Add(address);
            }
            foreach (
                var value in builder.Configuration.GetSection("Proxy:KnownNetworks").Get<string[]>()
                    ?? []
            )
            {
                if (
                    !System.Net.IPNetwork.TryParse(value, out var network)
                    || network.PrefixLength == 0
                )
                    throw new InvalidOperationException(
                        "Proxy:KnownNetworks must contain explicit non-wildcard CIDRs."
                    );
                forwarded.KnownIPNetworks.Add(network);
            }
            if (forwarded.KnownProxies.Count == 0 && forwarded.KnownIPNetworks.Count == 0)
                throw new InvalidOperationException(
                    "Proxy:KnownProxies or Proxy:KnownNetworks is required when trusting forwarded headers."
                );
            foreach (var host in allowedHosts)
                forwarded.AllowedHosts.Add(host);
            builder.Services.AddSingleton(forwarded);
        }
        if (builder.Configuration["Gateway:IdentityKey"] is not null)
            builder.Services.AddSingleton<GatewayIdentityCache>();
        else if (builder.Configuration.GetValue("Gateway:Required", false))
            throw new InvalidOperationException(
                "Gateway:IdentityKey is required when Gateway:Required is true."
            );
        // Operational fairness belongs to the gateway (ADR 54). This limiter
        // protects this process from in-flight work, including cold identity
        // lookups. No queue: callers receive an explicit overload response.
        var concurrency = builder.Configuration.GetValue("Traffic:MaxConcurrentRequests", 32);
        if (concurrency is < 1 or > 4096)
            throw new InvalidOperationException(
                "Traffic:MaxConcurrentRequests must be between 1 and 4096."
            );
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "1";
                return ValueTask.CompletedTask;
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetConcurrencyLimiter(
                    "process",
                    _ => new ConcurrencyLimiterOptions { PermitLimit = concurrency, QueueLimit = 0 }
                )
            );
        });
    }

    public static void UseRequestPolicies(this WebApplication app)
    {
        if (app.Configuration.GetValue("Proxy:TrustForwardedHeaders", false))
            app.UseForwardedHeaders(app.Services.GetRequiredService<ForwardedHeadersOptions>());
        app.UseMiddleware<UnhandledErrorMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        if (app.Configuration.GetValue("Gateway:Required", false))
            app.Use(
                async (context, next) =>
                {
                    if (
                        context.Request.Path != "/livez"
                        && context.Request.Path != "/healthz"
                        && !context
                            .RequestServices.GetRequiredService<GatewayIdentityCache>()
                            .IsTrusted(context)
                    )
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }
                    // Classification never becomes application authority.
                    context.Request.Headers.Remove(GatewayIdentityCache.PartitionHeader);
                    await next(context);
                }
            );
        app.UseWhen(
            context => context.Request.Path != "/livez" && context.Request.Path != "/healthz",
            limited => limited.UseRateLimiter()
        );
        if (app.Configuration["Gateway:IdentityKey"] is not null)
        {
            // Force configuration validation at boot, before serving traffic.
            _ = app.Services.GetRequiredService<GatewayIdentityCache>();
            app.Map(
                GatewayIdentityCache.Path,
                identity =>
                {
                    identity.UseMiddleware<GatewayIdentityCache>();
                    identity.UseAuthentication();
                    identity.UseMiddleware<SessionValidationMiddleware>();
                    identity.UseMiddleware<ApiKeyAuthenticationMiddleware>();
                    identity.Run(context =>
                    {
                        var principal = context
                            .RequestServices.GetRequiredService<IPrincipalAccessor>()
                            .Current;
                        var partition = principal switch
                        {
                            Principal.User { ActiveOrg: { } org } => $"org:{org.Value:D}",
                            Principal.Contact contact => $"org:{contact.Org.Value:D}",
                            Principal.Service service => $"org:{service.Org.Value:D}",
                            Principal.User user => $"user:{user.UserId:D}",
                            _ => null,
                        };
                        if (partition is null)
                        {
                            if (
                                !System.Net.IPAddress.TryParse(
                                    context
                                        .Request.Headers[GatewayIdentityCache.ClientIpHeader]
                                        .ToString(),
                                    out var ip
                                )
                            )
                            {
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                return Task.CompletedTask;
                            }
                            partition = $"ip:{ip}";
                        }
                        context.Response.Headers[GatewayIdentityCache.PartitionHeader] = partition;
                        return Task.CompletedTask;
                    });
                }
            );
        }
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
                api.UseMiddleware<SuspensionMiddleware>();
                api.UseMiddleware<IdempotencyMiddleware>();
                api.UseMiddleware<AccessLogMiddleware>();
            }
        );
    }
}
