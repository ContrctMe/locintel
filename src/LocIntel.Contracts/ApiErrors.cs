using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace LocIntel.Contracts;

/// <summary>
/// The one body a refused request carries: what went wrong, and the trace
/// id support can quote. The gates (GateResults) and the unhandled-error
/// middleware speak the same shape with their own extra fields; every other
/// refusal goes through here, and an architecture test refuses the
/// hand-built anonymous object that used to spell it a hundred times.
/// </summary>
/// <param name="Detail">Structured facts behind the refusal (what is over the limit, which orgs block), when a message is not enough.</param>
public sealed record ApiError(
    string Error,
    string? TraceId,
    string? Code = null,
    object? Detail = null
);

public static class ApiErrors
{
    public static IResult BadRequest(string message) =>
        Refuse(message, StatusCodes.Status400BadRequest);

    public static IResult NotFound(string message) =>
        Refuse(message, StatusCodes.Status404NotFound);

    public static IResult Conflict(string message) =>
        Refuse(message, StatusCodes.Status409Conflict);

    /// <summary>
    /// Transient admission contention (ADR 55). Admission locks never wait on a
    /// pooled connection, so the caller is the one that retries - and is told
    /// when, rather than being left to guess from a bare 503.
    /// </summary>
    public static IResult Busy(string message, int retryAfterSeconds = 1) =>
        new BusyResult(Body(message, "admission_busy"), retryAfterSeconds);

    /// <summary>Any other status (413, 422, 501): the same body under the status the caller names.</summary>
    public static IResult Status(
        string message,
        int status,
        string? code = null,
        object? detail = null
    ) => Refuse(message, status, code, detail);

    /// <summary>The body alone, for middleware that writes its own response; <paramref name="code"/> is the machine-readable reason.</summary>
    public static ApiError Body(string message, string? code = null, object? detail = null) =>
        new(message, Activity.Current?.TraceId.ToString(), code, detail);

    private sealed class BusyResult(ApiError body, int retryAfter) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = retryAfter.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            return Results
                .Json(body, statusCode: StatusCodes.Status503ServiceUnavailable)
                .ExecuteAsync(httpContext);
        }
    }

    private static IResult Refuse(
        string message,
        int status,
        string? code = null,
        object? detail = null
    ) => Results.Json(Body(message, code, detail), statusCode: status);
}
