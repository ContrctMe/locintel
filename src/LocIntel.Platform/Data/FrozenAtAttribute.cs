namespace LocIntel.Platform.Data;

/// <summary>
/// The moment a migration helper was frozen, as the UTC migration stamp
/// (yyyyMMddHHmmss) of the commit that froze it. A migration stamped at or
/// before this moment called the helper legitimately and can never be
/// edited; one stamped after it is a new use, which MigrationHelperTests
/// refuses. It is a moment, not a day: ADR 48 landed at 23:20 UTC and a
/// fork had three legitimate migrations from earlier that same day
/// (template feedback, round six, item 23). The stamp lives here, beside
/// the frozen text, so the test derives it rather than keeping its own.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class FrozenAtAttribute(string migrationStamp) : Attribute
{
    public string MigrationStamp { get; } = migrationStamp;
}
