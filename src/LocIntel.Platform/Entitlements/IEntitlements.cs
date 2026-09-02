using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.Entitlements;

/// <summary>
/// Gate 1 (ADRs 8/9/10): does the org's plan include the capability? Failure
/// is 402-and-upsell, never a 403. Evaluation never leaves the process - the
/// internal store is authoritative, sources sync in.
/// </summary>
public interface IEntitlements
{
    /// <summary>Boolean shape: the capability is on or off.</summary>
    ValueTask<bool> HasAsync(OrgId org, string code, CancellationToken ct = default);

    /// <summary>Tiered shape: the plan-determined value (retention days, export formats).</summary>
    ValueTask<string> ValueAsync(OrgId org, string code, CancellationToken ct = default);

    /// <summary>Numeric-limit shape resolved to its ceiling (hierarchy depth, max sites).</summary>
    ValueTask<long> LimitAsync(OrgId org, string code, CancellationToken ct = default);

    /// <summary>
    /// Limit shape enforcement at the creation point: current count + increment
    /// against the ceiling, applying the entitlement's declared policy.
    /// </summary>
    ValueTask<EntitlementDecision> CheckLimitAsync(
        OrgId org,
        string code,
        long current,
        long increment = 1,
        CancellationToken ct = default
    );

    /// <summary>
    /// Metered shape: append a usage event and decide per the policy.
    /// Append-then-rollup - the live count is approximate; Grace absorbs it.
    /// </summary>
    ValueTask<EntitlementDecision> RecordUsageAsync(
        OrgId org,
        string code,
        long amount = 1,
        CancellationToken ct = default
    );
}

public enum EntitlementOutcome
{
    Allowed,
    Warned,
    Overage,
    Blocked,
}

public sealed record EntitlementDecision(
    EntitlementOutcome Outcome,
    string Code,
    long Limit,
    long Current
)
{
    public bool IsAllowed => Outcome != EntitlementOutcome.Blocked;
}
