using System.Diagnostics;

namespace LocIntel.Api;

/// <summary>
/// The supportability floor for failures (maturity review, hole 1): every
/// response carries X-Trace-Id, and an unhandled exception becomes a 500
/// whose body includes the same id - so "it broke" tickets can quote
/// something that joins directly to the exported traces. The id is the W3C
/// trace id when tracing is active (it is - ADR 33), the connection id
/// otherwise.
/// </summary>
public sealed class UnhandledErrorMiddleware(
    RequestDelegate next,
    ILogger<UnhandledErrorMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.OnStarting(
            static state =>
            {
                var (http, id) = ((HttpContext, string))state;
                http.Response.Headers["X-Trace-Id"] = id;
                return Task.CompletedTask;
            },
            (context, traceId)
        );
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogError(exception, "unhandled exception (traceId {TraceId})", traceId);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(
                new
                {
                    error = "something went wrong on our side; if you contact support, quote the trace id",
                    traceId,
                }
            );
        }
    }
}
