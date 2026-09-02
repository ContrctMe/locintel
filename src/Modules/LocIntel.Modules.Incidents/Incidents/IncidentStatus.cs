using System.Text.Json.Serialization;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>Open until someone closes it with a reason; reopening clears the closure.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncidentStatus
{
    Open,
    Closed,
}
