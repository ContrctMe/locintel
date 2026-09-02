using System.Text.Json.Serialization;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>Where the report came in: a person in the console, an API key, an anonymous tip, or a connector.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncidentSource
{
    Console,
    Api,
    Tip,
    Import,
}
