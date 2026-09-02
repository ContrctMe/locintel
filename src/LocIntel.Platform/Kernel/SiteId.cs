namespace LocIntel.Platform.Kernel;

/// <summary>Strongly typed site identifier. UUIDv7 (ADR 35).</summary>
public readonly record struct SiteId(Guid Value)
{
    public static SiteId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
