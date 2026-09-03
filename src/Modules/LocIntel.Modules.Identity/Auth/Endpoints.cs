using System.Security.Claims;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Identity.Users;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Modules.Identity.Auth;

/// <summary>
/// Cookie-session auth flow (ADR 21): HttpOnly encrypted cookie issued by the
/// API after the code exchange; no token ever reaches JavaScript. Minimal APIs
/// rather than Wolverine endpoints because SignIn/SignOut are HttpContext
/// operations - Wolverine handlers stay for domain work.
/// </summary>
public static class AuthEndpoints
{
    private const string StatePurpose = "locintel.auth.state";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/auth/login",
            (
                HttpContext http,
                IAuthProvider provider,
                IDataProtectionProvider dp,
                string? returnUrl,
                string? hint,
                string? org,
                bool signup = false
            ) =>
            {
                var state = dp.CreateProtector(StatePurpose)
                    .Protect(
                        $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}|{SafeReturnUrl(returnUrl)}"
                    );
                var redirectUri = CallbackUri(http);
                return Results.Redirect(
                    provider.BuildAuthorizationUrl(
                        redirectUri,
                        state,
                        hint,
                        org,
                        signup ? "sign-up" : null
                    )
                );
            }
        );

        app.MapGet(
            "/auth/signup",
            async (IAuthProvider provider, string email, CancellationToken ct) =>
            {
                var trimmed = email.Trim().ToLowerInvariant();
                if (!trimmed.Contains('@') || trimmed.Length > 320)
                    return Results.BadRequest(new { error = "a valid email is required" });
                // AuthKit's hosted screen registers users itself; providers
                // that need the record first (the emulator, bare OIDC setups
                // with admin-created users) get it via the capability.
                if (provider is IUserProvisioning provisioning)
                    await provisioning.EnsureUserAsync(trimmed, ct);
                return Results.Redirect(
                    $"/auth/login?hint={Uri.EscapeDataString(trimmed)}&signup=true"
                );
            }
        );

        app.MapGet(
            "/auth/callback",
            async (
                HttpContext http,
                IAuthProvider provider,
                IDataProtectionProvider dp,
                IdentityDbContext db,
                string? code,
                string? state,
                string? error,
                CancellationToken ct
            ) =>
            {
                // OAuth error callback (cancelled login, unknown user, signup
                // disabled): no code arrives - hand the reason to the login
                // screen instead of crashing on a missing parameter.
                if (error is not null || code is null || state is null)
                {
                    var reason = new string(
                        (error ?? "missing_code")
                            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '_')
                            .ToArray()
                    );
                    return Results.Redirect($"/?authError={reason}");
                }

                string returnUrl;
                try
                {
                    var payload = dp.CreateProtector(StatePurpose).Unprotect(state).Split('|', 2);
                    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - long.Parse(payload[0]) > 600)
                        return Results.BadRequest(
                            new { error = "auth state expired, retry login" }
                        );
                    returnUrl = payload[1];
                }
                catch (Exception)
                {
                    return Results.BadRequest(new { error = "invalid auth state" });
                }

                var identity = await provider.ExchangeCodeAsync(code, CallbackUri(http), ct);

                var user = await db.Users.FirstOrDefaultAsync(
                    u => u.Provider == identity.Provider && u.Subject == identity.Subject,
                    ct
                );
                if (user is null)
                {
                    user = AppUser.Create(
                        identity.Provider,
                        identity.Subject,
                        identity.Email,
                        identity.Name
                    );
                    db.Users.Add(user);
                }
                else
                {
                    user.Email = identity.Email;
                    user.Name = identity.Name ?? user.Name;
                }

                // Provider-org mapping: an SSO login through an org connection joins
                // that org (JIT membership). Otherwise the user keeps existing memberships.
                await db.SaveChangesAsync(ct);
                if (
                    identity.ExternalOrgId is { } externalOrgId
                    && await db.OrgDirectory.FirstOrDefaultAsync(
                        d => d.ExternalId == externalOrgId,
                        ct
                    )
                        is { } mapped
                )
                {
                    // JIT join (invite acceptance lands here). The bootstrap
                    // reads RLS-protected tables, and THIS request's
                    // connection opened with no tenant - so it runs in a
                    // FRESH scope whose TenantContext carries the org
                    // (read-time rule: the answer must exist at connection
                    // open, and a new scope means a new connection).
                    await using var jitScope = http
                        .RequestServices.GetRequiredService<IServiceScopeFactory>()
                        .CreateAsyncScope();
                    jitScope
                        .ServiceProvider.GetRequiredService<TenantContext>()
                        .Set(mapped.OrgId, RegionId.Default);
                    var scopedDb = jitScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                    var scopedUser = await scopedDb.Users.FirstAsync(u => u.Id == user.Id, ct);
                    await Users.MembershipBootstrap.EnsureMembershipAsync(
                        scopedDb,
                        scopedUser,
                        mapped.OrgId,
                        ct
                    );
                    await scopedDb.SaveChangesAsync(ct);
                }

                var activeOrg = await db.DefaultOrgAsync(user.Id, ct);

                // server-side session record: the cookie's revocation authority
                var session = new Users.UserSession
                {
                    Id = Guid.CreateVersion7(),
                    UserId = user.Id,
                    UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 200),
                };
                db.Sessions.Add(session);
                await db.SaveChangesAsync(ct);

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    BuildClaimsPrincipal(user, activeOrg, session.Id)
                );
                return Results.Redirect(returnUrl);
            }
        );

        app.MapPost(
            "/auth/logout",
            async (HttpContext http, IdentityDbContext db, CancellationToken ct) =>
            {
                if (GetSessionId(http.User) is { } sid)
                    await db
                        .Sessions.Where(x => x.Id == sid && x.RevokedAt == null)
                        .ExecuteUpdateAsync(
                            u => u.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow),
                            ct
                        );
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.NoContent();
            }
        );

        app.MapPost(
            "/auth/switch-org",
            async (
                HttpContext http,
                IdentityDbContext db,
                SwitchOrgRequest request,
                CancellationToken ct
            ) =>
            {
                if (GetUserId(http.User) is not { } userId)
                    return Results.Unauthorized();

                var target = new OrgId(request.OrgId);
                // 404, not 403: same rule as tenant isolation - do not confirm the org exists.
                if (
                    !await db.Memberships.AnyAsync(m => m.UserId == userId && m.OrgId == target, ct)
                )
                    return Results.NotFound();

                var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    BuildClaimsPrincipal(user, target, GetSessionId(http.User))
                );
                return Results.NoContent();
            }
        );

        app.MapGet(
            "/me",
            async (
                IPrincipalAccessor accessor,
                IdentityDbContext db,
                IScopeResolver scopes,
                CancellationToken ct
            ) =>
            {
                switch (accessor.Current)
                {
                    case Principal.User u:
                    {
                        var orgIds = await db
                            .Memberships.Where(m => m.UserId == u.UserId)
                            .Select(m => m.OrgId)
                            .ToListAsync(ct);
                        // local read model (org_directory), never Tenancy's tables
                        // an impersonated org (ADR 42) is not a membership;
                        // surface it anyway so the console has a name to show
                        if (
                            u.Impersonating
                            && u.ActiveOrg is { } impersonated
                            && !orgIds.Contains(impersonated)
                        )
                            orgIds.Add(impersonated);
                        var summaries = await db
                            .OrgDirectory.Where(d => orgIds.Contains(d.OrgId))
                            .Select(d => new
                            {
                                id = d.OrgId.Value,
                                name = d.Name,
                                slug = d.Slug,
                                status = d.Status,
                                isPlatform = d.IsPlatform,
                            })
                            .ToListAsync(ct);
                        // resolved capabilities: the UI hides/disables instead
                        // of letting users discover 403s (the /me bootstrap)
                        var activeIsPlatform =
                            u.ActiveOrg is { } activeOrgId
                            && await db
                                .OrgDirectory.Where(d => d.OrgId == activeOrgId)
                                .Select(d => d.IsPlatform)
                                .FirstOrDefaultAsync(ct);
                        var capabilities = new List<string>();
                        foreach (var capability in Capabilities.All)
                        {
                            // the org flag is the operator wall: never
                            // advertise platform reach to an ordinary org
                            if (capability == Capabilities.PlatformOperate && !activeIsPlatform)
                                continue;
                            if (await scopes.CanAsync(u, capability, ct))
                                capabilities.Add(capability);
                        }
                        return Results.Ok(
                            new
                            {
                                tier = "user",
                                userId = u.UserId,
                                email = u.Email,
                                name = u.Name,
                                activeOrg = u.ActiveOrg?.Value,
                                organizations = summaries,
                                capabilities,
                                impersonationExpiresAt = u.ImpersonationExpiresAt,
                            }
                        );
                    }
                    case Principal.Contact c:
                        return Results.Ok(
                            new
                            {
                                tier = "contact",
                                contactId = c.ContactId,
                                email = c.Email,
                                org = c.Org.Value,
                            }
                        );
                    case Principal.Guest g:
                        return Results.Ok(new { tier = "guest", org = g.Org?.Value });
                    default:
                        return Results.Unauthorized();
                }
            }
        );

        return app;
    }

    public static ClaimsPrincipal BuildClaimsPrincipal(
        AppUser user,
        OrgId? activeOrg,
        Guid? sessionId,
        DateTimeOffset? impersonationExpires = null
    )
    {
        var claims = new List<Claim>
        {
            new(LocIntelClaims.UserId, user.Id.ToString()),
            new(LocIntelClaims.Email, user.Email),
            new(LocIntelClaims.Tier, "user"),
        };
        if (user.Name is { } name)
            claims.Add(new Claim(LocIntelClaims.DisplayName, name));
        if (activeOrg is { } org)
            claims.Add(new Claim(LocIntelClaims.ActiveOrg, org.Value.ToString()));
        if (sessionId is { } sid)
            claims.Add(new Claim(LocIntelClaims.SessionId, sid.ToString()));
        if (impersonationExpires is { } expires)
            claims.Add(
                new Claim(
                    LocIntelClaims.ImpersonationExpires,
                    expires.ToUnixTimeSeconds().ToString()
                )
            );
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
        );
    }

    /// <summary>
    /// A contact-tier session (ADR 7): the counterpart of the user issuer, so
    /// the claim set for "who is this request" is minted in one place and
    /// read back by one resolver. Contact links used to build this list by
    /// hand with a claim name only the resolver knew.
    /// </summary>
    public static ClaimsPrincipal BuildContactClaimsPrincipal(
        Guid contactId,
        OrgId org,
        string email
    )
    {
        var claims = new List<Claim>
        {
            new(LocIntelClaims.Tier, "contact"),
            new(LocIntelClaims.ContactId, contactId.ToString()),
            new(LocIntelClaims.ActiveOrg, org.Value.ToString()),
            new(LocIntelClaims.Email, email),
        };
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
        );
    }

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(LocIntelClaims.UserId), out var id) ? id : null;

    internal static Guid? GetSessionId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(LocIntelClaims.SessionId), out var id) ? id : null;

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null
        : value.Length <= max ? value
        : value[..max];

    private static string CallbackUri(HttpContext http) =>
        $"{http.Request.Scheme}://{http.Request.Host}/auth/callback";

    /// <summary>Open-redirect guard: relative paths only.</summary>
    /// <summary>
    /// Open-redirect guard: a same-site absolute PATH only. Requires a
    /// leading '/', rejects '//' (protocol-relative) AND '/\' (browsers
    /// normalize backslash to '/', so '/\evil.com' becomes '//evil.com').
    /// </summary>
    internal static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is ['/', ..]
        && !returnUrl.StartsWith("//")
        && !returnUrl.StartsWith("/\\")
        && !returnUrl.Contains('\\')
            ? returnUrl
            : "/";
}

public sealed record SwitchOrgRequest(Guid OrgId);
