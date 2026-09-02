using System.Text.Json.Serialization;

namespace LocIntel.Modules.Incidents.Incidents;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncidentSeverity
{
    Low,
    Medium,
    High,
    Critical,
}
