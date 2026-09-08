namespace LocIntel.Api;

/// <summary>Public routes still vary by authenticated principal and resolved tenant.</summary>
public sealed class PublicCacheMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/public"))
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                return Task.CompletedTask;
            });
        await next(context);
    }
}
