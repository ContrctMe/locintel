using System.Text.Json.Serialization;

namespace LocIntel.Modules.Cases.Cases;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CasePriority
{
    Low,
    Medium,
    High,
    Critical,
}
