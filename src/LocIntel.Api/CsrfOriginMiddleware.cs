namespace LocIntel.Api;

/// <summary>
/// CSRF defence-in-depth (security review). SameSite=Lax already strips the
/// session cookie from cross-site POSTs. This is the second layer: an unsafe-method
/// request that carries the session cookie AND an Origin header must have
/// that Origin match the request host. It needs no client change (the
/// console shares the API's origin, ADR 21) and no config.
///
/// Deliberately lenient where it must be: it only fires when the SESSION
/// cookie is present (or the request provisions a signup), so signature-authenticated webhooks and API-key
/// callers (which carry no cookie) are untouched; and a missing Origin
/// header passes, so native/non-browser clients are not blocked. Browsers
/// always send Origin on cross-origin unsafe requests, which is exactly the
/// case this catches.
/// </summary>
public sealed class CsrfOriginMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (
            !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method)
            && (
                context.Request.Cookies.ContainsKey("locintel_session")
                || context.Request.Path == "/auth/signup"
            )
            && context.Request.Headers.Origin.FirstOrDefault() is { Length: > 0 } origin
            && !OriginMatchesHost(origin, context.Request.Host)
        )
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { error = "cross-origin state change refused", code = "csrf_origin" }
            );
            return;
        }
        await next(context);
    }

    private static bool OriginMatchesHost(string origin, HostString host) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
        && string.Equals(originUri.Host, host.Host, StringComparison.OrdinalIgnoreCase)
        && (host.Port is null || originUri.Port == host.Port);
}
