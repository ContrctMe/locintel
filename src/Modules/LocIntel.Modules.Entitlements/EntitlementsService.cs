using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Entitlements;

/// <summary>
/// Gate-1 evaluation, fully in-process (ADR 10). Effective value precedence:
/// active exception > assigned value > catalog default. Metered counts are
/// rollups + uncompacted events for the current UTC calendar month.
/// </summary>
public sealed class EntitlementsService(EntitlementsDbContext db, TimeProvider time) : IEntitlements
{
    public async ValueTask<bool> HasAsync(OrgId org, string code, CancellationToken ct = default) =>
        bool.Parse(await EffectiveValueAsync(org, code, ct));

    public async ValueTask<string> ValueAsync(
        OrgId org,
        string code,
        CancellationToken ct = default
    ) => await EffectiveValueAsync(org, code, ct);

    public async ValueTask<long> LimitAsync(
        OrgId org,
        string code,
        CancellationToken ct = default
    ) => long.Parse(await EffectiveValueAsync(org, code, ct));

    public async ValueTask<EntitlementDecision> CheckLimitAsync(
        OrgId org,
        string code,
        long current,
        long increment = 1,
        CancellationToken ct = default
    )
    {
        var descriptor = Describe(code, EntitlementShape.Limit);
        var limit = await LimitAsync(org, code, ct);
        return Decide(descriptor, code, limit, current + increment);
    }

    public async ValueTask<EntitlementDecision> RecordUsageAsync(
        OrgId org,
        string code,
        long amount = 1,
        CancellationToken ct = default
    )
    {
        var descriptor = Describe(code, EntitlementShape.Metered);
        var limit = await LimitAsync(org, code, ct);
        var now = time.GetUtcNow();
        var used = await CurrentPeriodUsageAsync(org, code, now, ct);

        var decision = Decide(descriptor, code, limit, used + amount);
        if (decision.IsAllowed)
        {
            db.UsageEvents.Add(
                new UsageEvent
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org,
                    Code = code,
                    Amount = amount,
                    OccurredAt = now,
                }
            );
            await db.SaveChangesAsync(ct);
        }
        return decision;
    }

    public async Task<long> CurrentPeriodUsageAsync(
        OrgId org,
        string code,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var month = new DateOnly(now.Year, now.Month, 1);
        var periodStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var rolled =
            await db
                .Rollups.Where(r => r.OrgId == org && r.Code == code && r.PeriodMonth == month)
                .SumAsync(r => (long?)r.Amount, ct)
            ?? 0;
        var compactedThrough =
            await db
                .Rollups.Where(r => r.OrgId == org && r.Code == code && r.PeriodMonth == month)
                .MaxAsync(r => (DateTimeOffset?)r.CompactedThrough, ct)
            ?? periodStart;
        var live =
            await db
                .UsageEvents.Where(e =>
                    e.OrgId == org && e.Code == code && e.OccurredAt >= compactedThrough
                )
                .SumAsync(e => (long?)e.Amount, ct)
            ?? 0;
        return rolled + live;
    }

    /// <summary>
    /// The full tenant-facing picture: effective value, shape, policy, and -
    /// where the system can know it - CURRENT USAGE (metered codes from the
    /// period counter, limit codes from their registered probe). "You've used
    /// 340 of 1,000" beats a bare "1000" (UX gap: usage visibility).
    /// </summary>
    public async Task<Dictionary<string, object>> DescribeAllAsync(
        OrgId org,
        IEnumerable<IEntitlementUsageProbe> probes,
        CancellationToken ct
    )
    {
        var probeByCode = probes.ToDictionary(p => p.Code);
        var now = time.GetUtcNow();
        var effective = new Dictionary<string, object>();
        foreach (var (code, descriptor) in EntitlementCatalog.Definitions)
        {
            long? usage = descriptor.Shape switch
            {
                EntitlementShape.Metered => await CurrentPeriodUsageAsync(org, code, now, ct),
                EntitlementShape.Limit when probeByCode.TryGetValue(code, out var probe) =>
                    await probe.CurrentUsageAsync(org, ct),
                _ => null,
            };
            effective[code] = new
            {
                value = await EffectiveValueAsync(org, code, ct),
                shape = descriptor.Shape.ToString(),
                policy = descriptor.Policy.ToString(),
                usage,
            };
        }
        return effective;
    }

    public async Task<string> EffectiveValueAsync(OrgId org, string code, CancellationToken ct)
    {
        var descriptor = Describe(code, shape: null);
        var now = time.GetUtcNow();
        var exception = await db
            .Exceptions.Where(e => e.OrgId == org && e.Code == code && e.ExpiresAt > now)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Value)
            .FirstOrDefaultAsync(ct);
        if (exception is not null)
            return exception;
        var assigned = await db
            .OrgEntitlements.Where(e => e.OrgId == org && e.Code == code)
            .Select(e => e.Value)
            .FirstOrDefaultAsync(ct);
        return assigned ?? descriptor.DefaultValue;
    }

    private static EntitlementDescriptor Describe(string code, EntitlementShape? shape)
    {
        if (!EntitlementCatalog.Definitions.TryGetValue(code, out var descriptor))
            throw new InvalidOperationException($"Unknown entitlement code '{code}'.");
        if (shape is { } expected && descriptor.Shape != expected)
            throw new InvalidOperationException(
                $"Entitlement '{code}' is {descriptor.Shape}, not {expected}."
            );
        return descriptor;
    }

    private static EntitlementDecision Decide(
        EntitlementDescriptor descriptor,
        string code,
        long limit,
        long wouldBe
    )
    {
        if (wouldBe <= limit)
            return new EntitlementDecision(EntitlementOutcome.Allowed, code, limit, wouldBe);
        return descriptor.Policy switch
        {
            LimitPolicy.Block => new(EntitlementOutcome.Blocked, code, limit, wouldBe),
            LimitPolicy.Grace when wouldBe <= limit * EntitlementCatalog.GraceFactor => new(
                EntitlementOutcome.Warned,
                code,
                limit,
                wouldBe
            ),
            LimitPolicy.Grace => new(EntitlementOutcome.Blocked, code, limit, wouldBe),
            LimitPolicy.Overage => new(EntitlementOutcome.Overage, code, limit, wouldBe),
            _ => new(EntitlementOutcome.Warned, code, limit, wouldBe),
        };
    }
}
