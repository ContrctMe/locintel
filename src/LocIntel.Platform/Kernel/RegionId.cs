namespace LocIntel.Platform.Kernel;

/// <summary>
/// Deployment region an org's data lives in (ADR 35). v1 runs a single regional
/// silo, but region is resolved explicitly on every data access from day one so
/// multi-region routing later is an addition, not a rewrite.
/// </summary>
public readonly record struct RegionId(string Value)
{
    public static readonly RegionId Default = new("primary");

    public override string ToString() => Value;
}
