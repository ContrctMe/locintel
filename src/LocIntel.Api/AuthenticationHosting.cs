using LocIntel.Integrations.WorkOS;
using LocIntel.Modules.Identity.Auth;
using LocIntel.Platform.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using static LocIntel.Api.ProviderOptionsValidation;

namespace LocIntel.Api;

internal static class AuthenticationHosting
{
    public static void AddAuthenticationHosting(this WebApplicationBuilder builder, string role)
    {
        // Auth seam (ADR 14): provider selected by config; WorkOS is the built-in
        // full-capability implementation, local is the dev/test base implementation.
        var authProvider = builder.Configuration["Auth:Provider"] ?? "local";
        switch (authProvider)
        {
            case "workos":
                builder
                    .Services.AddOptions<WorkOSOptions>()
                    .Bind(builder.Configuration.GetSection("Auth:WorkOS"))
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.ApiKey),
                        "Auth:WorkOS:ApiKey is required."
                    )
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.ClientId),
                        "Auth:WorkOS:ClientId is required."
                    )
                    .Validate(
                        o => IsHttpUrl(o.ApiBaseUrl),
                        "Auth:WorkOS:ApiBaseUrl must be an absolute HTTP(S) URL."
                    )
                    .ValidateOnStart();
                builder.Services.AddSingleton<IAuthProvider, WorkOSAuthProvider>();
                break;
            case "local" when !builder.Environment.IsProduction():
                builder.Services.AddSingleton<IAuthProvider, LocalAuthProvider>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Auth:Provider '{authProvider}' is not valid for {builder.Environment.EnvironmentName}. "
                        + "Use 'workos' in Production; 'local' is dev/test only (ADR 14)."
                );
        }

        // Data protection (security review): the keyring protects auth-ticket
        // cookies AND contact magic-link tokens. The framework default is a
        // per-process filesystem keyring, unencrypted - which means (a) across
        // REPLICAS a cookie/token minted by one instance cannot be read by
        // another (broken sessions and dead magic links behind a load balancer),
        // and (b) keys vanish on a fresh container. A shared, protected store is
        // therefore REQUIRED in any multi-replica deployment.
        //
        // The application name is pinned so a shared ring is unambiguous; the
        // persistence directory (a mounted volume or network path all replicas
        // share) is config-driven, and Production REFUSES to boot on the
        // ephemeral default rather than silently breaking sessions after the
        // first scale-out. Forks on a cloud should point this at a blob/secret
        // store and wrap it with their KMS (see docs/production.md).
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName("locintel");
        if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        else if (builder.Environment.IsProduction() && role != "migrate")
            throw new InvalidOperationException(
                "DataProtection:KeyPath is required in Production (a store all replicas share); "
                    + "the default per-process keyring breaks sessions and magic links after scale-out."
            );

        // Cookie session (ADR 21): HttpOnly, no token ever reachable from JS.
        builder
            .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "locintel_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                // Production is a hard floor: Always, so a fork that forgets to
                // trust its proxy's X-Forwarded-Proto gets broken logins (loud)
                // instead of session cookies over plain HTTP (silent). Elsewhere
                // SameAsRequest keeps http://localhost working.
                options.Cookie.SecurePolicy = builder.Environment.IsProduction()
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                // API, not a browser app: never redirect to a login page.
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = 401;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = 403;
                    return Task.CompletedTask;
                };
            });
    }
}
