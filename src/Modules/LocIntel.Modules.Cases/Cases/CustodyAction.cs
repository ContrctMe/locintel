using System.Text.Json.Serialization;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Every touch of a piece of evidence, in the order it happened.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustodyAction
{
    Added,
    Downloaded, // Historical entries: retained, not rewritten as proof of delivery.
    Exported,
    Removed,
    HoldPlaced,
    HoldReleased,
    DownloadAccessIssued,
}
