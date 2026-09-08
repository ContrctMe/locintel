using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Kernel;

namespace LocIntel.IntegrationTests;

/// <summary>How many times a request asked Tenancy to resolve sites.</summary>
public sealed class SiteSourceCalls
{
    private int _selects;

    public int Selects => Volatile.Read(ref _selects);

    public void Reset() => Volatile.Write(ref _selects, 0);

    public void Record() => Interlocked.Increment(ref _selects);
}

/// <summary>
/// Counts site resolutions so a run listing can prove it batches them. Registered
/// by type over the concrete Tenancy implementation - a lambda factory would be
/// service location, which Wolverine's code generation refuses.
/// </summary>
public sealed class CountingSiteSource(SiteSource inner, SiteSourceCalls calls) : ISiteSource
{
    public Task<IReadOnlyList<ISiteSource.Site>> SelectAsync(
        OrgId org,
        NodeScope scope,
        Guid[]? ids,
        int limit,
        CancellationToken ct = default
    )
    {
        calls.Record();
        return inner.SelectAsync(org, scope, ids, limit, ct);
    }

    public Task<IReadOnlyList<ISiteSource.Hours>> HoursAsync(
        OrgId org,
        NodeScope scope,
        Guid[] ids,
        DateOnly from,
        DateOnly through,
        CancellationToken ct = default
    ) => inner.HoursAsync(org, scope, ids, from, through, ct);
}
