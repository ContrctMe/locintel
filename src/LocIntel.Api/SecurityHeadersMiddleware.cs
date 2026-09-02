namespace LocIntel.Api;

/// <summary>
/// The HTTP hardening floor (operability item 8). The API serves JSON and
/// redirects, so the CSP is maximally strict - nothing loads, nothing
/// frames. The FRONTENDS are static bundles on their own hosts; their CSPs
/// belong to the host serving them (docs/production.md says so). HSTS is
/// the reverse proxy's job in the documented topology, so it is not set
/// here - a proxy-level header would double it.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(
            static state =>
            {
                var headers = ((HttpContext)state).Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                return Task.CompletedTask;
            },
            context
        );
        await next(context);
    }
}
