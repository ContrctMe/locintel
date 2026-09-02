using System.Security.Cryptography;

namespace LocIntel.Api;

/// <summary>
/// Guests are principals (ADR 7), so they get a session too: an opaque random
/// cookie that is the rate-limit subject (ADR 30) and, later, the CSRF anchor.
/// Not an auth cookie - it identifies a browser, not a person.
/// </summary>
public sealed class GuestSessionMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public const string CookieName = "locintel_guest";

    public async Task InvokeAsync(HttpContext context)
    {
        if (
            context.User.Identity?.IsAuthenticated != true
            && !context.Request.Cookies.ContainsKey(CookieName)
        )
        {
            context.Response.Cookies.Append(
                CookieName,
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    // same policy as the session cookie: Secure whenever the
                    // (forwarded-header-resolved) request is HTTPS, and a
                    // hard floor in Production
                    Secure = context.Request.IsHttps || environment.IsProduction(),
                    MaxAge = TimeSpan.FromDays(30),
                    IsEssential = true,
                }
            );
        }
        await next(context);
    }
}
