using System.Security.Cryptography;
using LocIntel.Platform.Infra;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Api;

/// <summary>
/// ADR 29: Idempotency-Key on every unsafe method. Same key + same request
/// replays the stored response; same key + different body is a 422; a still
/// in-flight original answers 409. Keyed per org; 24h TTL enforced by the
/// cleanup job through a dedicated RLS delete policy.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    private const int MaxStoredBody = 256 * 1024;

    public async Task InvokeAsync(
        HttpContext context,
        PlatformDbContext db,
        IPrincipalAccessor accessor
    )
    {
        if (
            context.Request.Method is not ("POST" or "PUT" or "PATCH" or "DELETE")
            || !context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues)
            || keyValues.ToString() is not { Length: > 0 and <= 200 } key
        )
        {
            await next(context);
            return;
        }
        // the key needs a subject: only org-scoped principals are idempotency-tracked
        if (accessor.Current is not Principal.User { ActiveOrg: { } org })
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        var hash = await HashRequestAsync(context.Request);

        var existing = await db.IdempotencyRecords.FirstOrDefaultAsync(r =>
            r.OrgId == org && r.Key == key
        );
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
            {
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Idempotency-Key was already used with a different request" }
                );
                return;
            }
            if (existing.StatusCode is not { } storedStatus)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        error = "the original request with this Idempotency-Key is still in flight",
                    }
                );
                return;
            }
            context.Response.StatusCode = storedStatus;
            if (existing.ContentType is { } contentType)
                context.Response.ContentType = contentType;
            if (existing.Body is { } body)
                await context.Response.Body.WriteAsync(body);
            return;
        }

        db.IdempotencyRecords.Add(
            new IdempotencyRecord
            {
                OrgId = org,
                Key = key,
                Endpoint = $"{context.Request.Method} {context.Request.Path}",
                RequestHash = hash,
            }
        );
        await db.SaveChangesAsync();

        // capture the response for replay
        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = original;
        }
        buffer.Position = 0;
        await buffer.CopyToAsync(original);

        var record = await db.IdempotencyRecords.FirstAsync(r => r.OrgId == org && r.Key == key);
        record.StatusCode = context.Response.StatusCode;
        record.ContentType = context.Response.ContentType;
        record.Body = buffer.Length <= MaxStoredBody ? buffer.ToArray() : null;
        await db.SaveChangesAsync();
    }

    private static async Task<string> HashRequestAsync(HttpRequest request)
    {
        using var sha = SHA256.Create();
        var prefix = System.Text.Encoding.UTF8.GetBytes($"{request.Method} {request.Path}\n");
        sha.TransformBlock(prefix, 0, prefix.Length, null, 0);
        var bodyBuffer = new byte[64 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(bodyBuffer)) > 0)
            sha.TransformBlock(bodyBuffer, 0, read, null, 0);
        sha.TransformFinalBlock([], 0, 0);
        request.Body.Position = 0;
        return Convert.ToHexStringLower(sha.Hash!);
    }
}

/// <summary>Hourly TTL sweep, leased per period like every sweep; the work is CleanupIdempotencyHandler.</summary>
public sealed class IdempotencyCleanupService(IServiceProvider services)
    : LocIntel.Platform.Messaging.GlobalSweepService<CleanupIdempotency>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);
}
