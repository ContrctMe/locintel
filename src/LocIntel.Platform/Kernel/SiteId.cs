namespace LocIntel.Platform.Kernel;

/// <summary>Strongly typed site identifier. UUIDv7 (ADR 35).</summary>
public readonly record struct SiteId(Guid Value) : IComparable<SiteId>
{
    public static SiteId New() => new(Guid.CreateVersion7());

    /// <summary>
    /// Ordered as Postgres orders uuid (bytewise), so a keyset cursor on
    /// (name, id) reads the same on both sides of the query.
    /// </summary>
    public int CompareTo(SiteId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();
}
