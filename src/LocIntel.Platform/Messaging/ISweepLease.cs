namespace LocIntel.Platform.Messaging;

/// <summary>
/// Who runs this period's sweep. Every worker replica ticks its own timer;
/// the first to claim (sweep, period) runs it and the rest skip, so N
/// replicas produce one logical sweep per period without a leader.
/// </summary>
public interface ISweepLease
{
    /// <summary>True exactly once per (sweep, period) across every replica.</summary>
    ValueTask<bool> TryClaimAsync(string sweep, TimeSpan interval, CancellationToken ct = default);
}
