using System.Collections.Concurrent;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;

namespace LocIntel.Api;

/// <summary>
/// ADR 30: the per-org request quota reads the api.requests_per_minute
/// entitlement - but rate-limit partitioning is synchronous, so values are
/// cached and refreshed in the background. Until the first resolution an org
/// gets the catalog default; a five-minute TTL bounds staleness after a plan
/// change.
/// </summary>
public sealed class OrgRateLimitCache(
    IServiceScopeFactory scopeFactory,
    ILogger<OrgRateLimitCache> logger
)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<OrgId, (int limit, DateTimeOffset at)> _cache = new();
    private readonly ConcurrentDictionary<OrgId, bool> _refreshing = new();

    public int LimitFor(OrgId org)
    {
        if (_cache.TryGetValue(org, out var entry) && DateTimeOffset.UtcNow - entry.at < Ttl)
            return entry.limit;
        if (_refreshing.TryAdd(org, true))
            _ = RefreshAsync(org);
        return entry.limit > 0
            ? entry.limit
            : (int)
                EntitlementCatalog
                    .Definitions[EntitlementCatalog.ApiRequestsPerMinute]
                    .DefaultAsLong;
    }

    private async Task RefreshAsync(OrgId org)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            // the refresh reads RLS-protected rows, so the fresh scope MUST
            // carry the org (found by the load baseline: without this the
            // read saw an empty table and cached the catalog default - the
            // per-org quota entitlement was silently inert for every org)
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(org, RegionId.Default);
            var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlements>();
            var limit = await entitlements.LimitAsync(org, EntitlementCatalog.ApiRequestsPerMinute);
            _cache[org] = ((int)limit, DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            // keep the previous/default value; next request retries - but
            // LOUDLY: a silent failure here quietly throttles a paying org
            // at the free-tier default
            logger.LogWarning(
                exception,
                "org rate-limit refresh failed for {OrgId}; serving previous/default",
                org.Value
            );
        }
        finally
        {
            _refreshing.TryRemove(org, out _);
        }
    }
}
