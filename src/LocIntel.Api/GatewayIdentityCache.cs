using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace LocIntel.Api;

/// <summary>
/// Private, bounded cache of traffic classification, never authorization.
/// Actual requests still run the complete authentication and gate pipeline.
/// </summary>
public sealed class GatewayIdentityCache : IMiddleware, IDisposable
{
    public const string Path = "/_gateway/identity";
    public const string KeyHeader = "X-LocIntel-Gateway-Key";
    public const string PartitionHeader = "X-LocIntel-Fairness-Partition";
    public const string ClientIpHeader = "X-LocIntel-Client-IP";

    private readonly byte[] _key;
    private readonly TimeSpan _ttl;
    private readonly MemoryCache _cache;

    public GatewayIdentityCache(IConfiguration configuration)
    {
        var key = configuration["Gateway:IdentityKey"] ?? "";
        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException(
                "Gateway:IdentityKey must contain at least 32 bytes."
            );
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var seconds = configuration.GetValue("Gateway:IdentityCacheSeconds", 15);
        var entries = configuration.GetValue("Gateway:IdentityCacheEntries", 10000);
        if (seconds is < 1 or > 60 || entries is < 1 or > 100000)
            throw new InvalidOperationException(
                "Gateway identity cache requires 1–60 seconds and 1–100000 entries."
            );
        _ttl = TimeSpan.FromSeconds(seconds);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = entries });
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!IsTrusted(context))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        // External-auth protocols may preserve the original method/path; this
        // branch only classifies headers and never executes that operation.
        // Both credentials participate: cookie precedence and org switches must
        // match the application's normal authentication pipeline.
        var cacheKey = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(
                        new[]
                        {
                            context.Request.Headers.Cookie.ToString(),
                            context.Request.Headers.Authorization.ToString(),
                        }
                    )
                )
            )
        );
        if (_cache.TryGetValue<string>(cacheKey, out var partition))
        {
            context.Response.Headers[PartitionHeader] = partition;
            return;
        }
        await next(context);
        partition = context.Response.Headers[PartitionHeader].ToString();
        if (
            context.Response.StatusCode == 200
            && (
                partition.StartsWith("org:", StringComparison.Ordinal)
                || partition.StartsWith("user:", StringComparison.Ordinal)
            )
        )
            // ponytail: concurrent cold misses may duplicate the identity read;
            // add request coalescing only if measured cache-miss bursts require it.
            _cache.Set(
                cacheKey,
                partition,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl, Size = 1 }
            );
    }

    public bool IsTrusted(HttpContext context) =>
        CryptographicOperations.FixedTimeEquals(
            _key,
            SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers[KeyHeader].ToString()))
        );

    public void Dispose() => _cache.Dispose();
}
