using System.Text.Json.Serialization;

namespace LocIntel.Modules.Marketplace.Marketplace;

/// <summary>Emergency is dispatched now; Scheduled has a window; Standing recurs (RRULE, ADR 27).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestUrgency
{
    Emergency,
    Scheduled,
    Standing,
}
