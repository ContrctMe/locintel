using LocIntel.Contracts;
using LocIntel.Platform.Audit;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Wolverine;

namespace LocIntel.Api;

/// <summary>
/// Authz-decision capture (ADR 12) as a decorator at the IScopeResolver port:
/// denials always (the floor - they answer "why can't she see this?" and are
/// the probing early-warning), grants only when policy asks. Published to the
/// durable queue with actor on the headers and tenant on the envelope.
/// </summary>
public sealed class AuditedScopeResolver(
    LocIntel.Modules.Identity.Access.GrantScopeResolver inner,
    IMessageBus bus,
    AuditPolicyCache policies
) : IScopeResolver
{
    public async ValueTask<bool> CanAsync(
        Principal principal,
        string action,
        CancellationToken ct = default
    ) => await ScopeForAsync(principal, action, ct) is not NodeScope.None;

    public async ValueTask<NodeScope> ScopeForAsync(
        Principal principal,
        string action,
        CancellationToken ct = default
    )
    {
        var scope = await inner.ScopeForAsync(principal, action, ct);
        if (principal is Principal.User { ActiveOrg: { } org, UserId: var userId })
        {
            var denied = scope is NodeScope.None;
            if (denied || policies.For(org).LogGrants)
            {
                await bus.PublishAsync(
                    new RecordAuthzAudit(action, denied ? "denied" : "granted", Summarize(scope)),
                    new DeliveryOptions
                    {
                        TenantId = org.Value.ToString(),
                        Headers =
                        {
                            [AuditHeaders.Tier] = "user",
                            [AuditHeaders.ActorId] = userId.ToString(),
                        },
                    }
                );
            }
        }
        return scope;
    }

    private static string Summarize(NodeScope scope) =>
        scope switch
        {
            NodeScope.EntireOrg => "entire-org",
            NodeScope.Subtrees s => $"subtrees:{s.Paths.Count}",
            _ => "none",
        };
}

/// <summary>
/// Read/access logging (ADR 13's async, high-volume path): GET/HEAD only,
/// only when the effective policy enables it, fire-and-forget onto the
/// durable queue. Skips infrastructure paths.
/// </summary>
public sealed class AccessLogMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IPrincipalAccessor accessor,
        AuditPolicyCache policies,
        IMessageBus bus
    )
    {
        await next(context);

        if (context.Request.Method is not ("GET" or "HEAD"))
            return;
        var path = context.Request.Path.Value ?? "/";
        if (path is "/healthz" || path.StartsWith("/auth/"))
            return;
        var (org, tier, actorId) = accessor.Current switch
        {
            Principal.User u when u.ActiveOrg is { } o => ((OrgId?)o, "user", (Guid?)u.UserId),
            Principal.Contact c => (c.Org, "contact", c.ContactId),
            Principal.Guest { Org: { } o } => ((OrgId?)o, "guest", null),
            _ => (null, "", null),
        };
        if (org is not { } orgId || !policies.For(orgId).LogReads)
            return;

        var options = new DeliveryOptions
        {
            TenantId = orgId.Value.ToString(),
            Headers = { [AuditHeaders.Tier] = tier },
        };
        if (actorId is { } id)
            options.Headers[AuditHeaders.ActorId] = id.ToString();
        await bus.PublishAsync(
            new RecordAccessAudit(context.Request.Method, path, context.Response.StatusCode),
            options
        );
    }
}

/// <summary>
/// Refresh-behind policy cache: audit
/// decisions sit on hot paths and cannot await a per-request policy query.
/// Floor until first resolution; five-minute TTL bounds staleness.
/// </summary>
public sealed class AuditPolicyCache(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration
)
{
    private readonly TimeSpan Ttl = TimeSpan.FromSeconds(
        configuration.GetValue("Audit:PolicyCacheTtlSeconds", 300)
    );
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        OrgId,
        (AuditPolicy policy, DateTimeOffset at)
    > _cache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<OrgId, bool> _refreshing =
        new();

    public AuditPolicy For(OrgId org)
    {
        if (_cache.TryGetValue(org, out var entry) && DateTimeOffset.UtcNow - entry.at < Ttl)
            return entry.policy;
        if (_refreshing.TryAdd(org, true))
            _ = RefreshAsync(org);
        return entry.policy ?? AuditPolicy.Floor;
    }

    private async Task RefreshAsync(OrgId org)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var provider = scope.ServiceProvider.GetRequiredService<IAuditPolicyProvider>();
            _cache[org] = (await provider.GetAsync(org), DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            // keep floor/previous; next request retries
        }
        finally
        {
            _refreshing.TryRemove(org, out _);
        }
    }
}
