namespace LocIntel.Platform.Kernel;

/// <summary>
/// Strongly typed organization identifier. UUIDv7 (ADR 35): time-ordered for
/// index locality, globally unique so ids never collide across regions.
/// </summary>
public readonly record struct OrgId(Guid Value)
{
    public static OrgId New() => new(Guid.CreateVersion7());

    public static OrgId Parse(string s) => new(Guid.Parse(s));

    public override string ToString() => Value.ToString();
}
