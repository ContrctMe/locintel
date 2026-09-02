using System.Text.Json.Serialization;

namespace LocIntel.Modules.Network.Shares;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShareStatus
{
    Active,
    Closed,
}
