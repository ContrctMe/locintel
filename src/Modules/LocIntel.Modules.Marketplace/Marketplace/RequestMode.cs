using System.Text.Json.Serialization;

namespace LocIntel.Modules.Marketplace.Marketplace;

/// <summary>Direct: one chosen vendor. Broadcast: a request for quotes to every matching vendor; the buyer awards one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestMode
{
    Direct,
    Broadcast,
}
