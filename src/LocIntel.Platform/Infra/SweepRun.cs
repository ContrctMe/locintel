namespace LocIntel.Platform.Infra;

/// <summary>
/// One row per (sweep, period): the durable identity of a scheduled run.
/// Whichever worker replica inserts it first owns that period's sweep; the
/// others see the conflict and skip. Deletion tier 3: rows older than a
/// month are hard-deleted by the idempotency cleanup sweep.
/// </summary>
public sealed class SweepRun
{
    public required string Sweep { get; init; }

    /// <summary>UTC instant (ADR 26): the start of the period bucket, aligned to the epoch.</summary>
    public required DateTimeOffset Period { get; init; }

    /// <summary>UTC instant (ADR 26): when the claim landed.</summary>
    public required DateTimeOffset ClaimedAt { get; init; }

    /// <summary>Which process won - for the runbook, not for logic.</summary>
    public required string ClaimedBy { get; init; }
}
