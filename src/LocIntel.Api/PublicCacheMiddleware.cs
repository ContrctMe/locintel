namespace LocIntel.Api;

/// <summary>
/// A short cache window on the public read surface (review item: the one
/// surface built for crawler and embed traffic had no caching story). Only
/// successful GETs under /public get it, so a CDN or reverse proxy in front
/// of the API absorbs repeat hits and bounds the per-request SSR+DB cost.
/// The TTL is deliberately short: open/closed status flips at window
/// boundaries, and stale-while-revalidate keeps it responsive past expiry.
/// </summary>
public sealed class PublicCacheMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (
            HttpMethods.IsGet(context.Request.Method)
            && context.Request.Path.StartsWithSegments("/public")
        )
            context.Response.OnStarting(
                static state =>
                {
                    var http = (HttpContext)state;
                    if (http.Response.StatusCode == StatusCodes.Status200OK)
                        http.Response.Headers.CacheControl =
                            "public, max-age=60, stale-while-revalidate=300";
                    return Task.CompletedTask;
                },
                context
            );
        await next(context);
    }
}
