using System.Text.Json.Serialization;

namespace LocIntel.Modules.Reporting;

/// <summary>
/// What a single request produces. Aggregate and bulk are different products, not
/// two names for one fan-out (ADR 56). Wire and column values are the lowercase
/// member name; both predate the enum, so neither may drift from it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportMode>))]
public enum ReportMode
{
    [JsonStringEnumMemberName("single")]
    Single,

    [JsonStringEnumMemberName("bulk")]
    Bulk,

    [JsonStringEnumMemberName("aggregate")]
    Aggregate,
}
